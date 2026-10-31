// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.InteropServices;
using System.Threading.Channels;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.WindowsAndMessaging;
using WNDPROC = Windows.Win32.Extras.ManagedWNDPROC;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Owns one hidden Shell notification window for all folder subscriptions of a source.
/// </summary>
internal sealed class WindowsShellChangeWatcher : IAsyncDisposable
{
	private const string WindowClassPrefix = "FilesCoreShellChangeWatcher";

	private const SHCNE_ID ChangeMask =
		SHCNE_ID.SHCNE_CREATE
		| SHCNE_ID.SHCNE_DELETE
		| SHCNE_ID.SHCNE_MKDIR
		| SHCNE_ID.SHCNE_RMDIR
		| SHCNE_ID.SHCNE_RENAMEITEM
		| SHCNE_ID.SHCNE_RENAMEFOLDER
		| SHCNE_ID.SHCNE_UPDATEITEM
		| SHCNE_ID.SHCNE_UPDATEDIR;

	private const int SubscriptionCapacity = 256;

	private readonly IWindowsShellScheduler _scheduler;
	private readonly string _windowClassName = $"{WindowClassPrefix}_{Guid.NewGuid():N}";
	private readonly List<WindowsShellFolderChangeRegistration> _registrations = [];
	private readonly Lock _disposalLock = new();
	private WNDPROC? _wndProc;
	private HWND _window;
	private uint _notificationMessage;
	private Task? _disposeTask;
	private int _isDisposed;

	internal WindowsShellChangeWatcher(IWindowsShellScheduler scheduler)
	{
		ArgumentNullException.ThrowIfNull(scheduler);

		_scheduler = scheduler;
	}

	internal unsafe Task<WindowsShellChangeSubscription> SubscribeAsync(WindowsItemLocator folderLocator, bool recursive, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(folderLocator);

		return _scheduler.InvokeAsync(() =>
		{
			ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

			if (folderLocator.AbsolutePidl.IsEmpty)
			{
				throw new InvalidOperationException("The folder does not have an absolute PIDL.");
			}

			var folderPidl = folderLocator.AbsolutePidl.ToArray();
			var existingRegistration = FindRegistration(folderPidl, recursive);
			if (existingRegistration is not null)
			{
				return existingRegistration.CreateSubscription(this);
			}

			if (_window.IsNull)
			{
				fixed (char* className = _windowClassName)
				{
					_wndProc ??= new(WindowProc);

					WNDCLASSEXW windowClass = default;
					windowClass.cbSize = (uint)sizeof(WNDCLASSEXW);
					windowClass.hInstance = PInvoke.GetModuleHandle(default(PCWSTR));
					windowClass.lpszClassName = className;
					windowClass.lpfnWndProc = (delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, LRESULT>)Marshal.GetFunctionPointerForDelegate(_wndProc);

					if (PInvoke.RegisterClassEx(&windowClass) == 0)
					{
						throw new InvalidOperationException("RegisterClassEx failed.");
					}

					_notificationMessage = PInvoke.RegisterWindowMessage(_windowClassName);
					if (_notificationMessage == 0)
					{
						PInvoke.UnregisterClass(className, windowClass.hInstance);
						throw new InvalidOperationException("RegisterWindowMessage failed.");
					}

					_window = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_LEFT, className, default, WINDOW_STYLE.WS_OVERLAPPED, 0, 0, 1, 1, HWND.Null, HMENU.Null, HINSTANCE.Null, null);

					if (_window.IsNull)
					{
						PInvoke.UnregisterClass(className, windowClass.hInstance);
						throw new InvalidOperationException("CreateWindowEx failed.");
					}
				}
			}

			var nativePidl = WindowShellHelpers.AllocatePidl(folderPidl);
			SHChangeNotifyEntry entry = default;
			entry.pidl = nativePidl;
			entry.fRecursive = recursive;
			var registrationId = PInvoke.SHChangeNotifyRegister(_window, SHCNRF_SOURCE.SHCNRF_ShellLevel | SHCNRF_SOURCE.SHCNRF_InterruptLevel | SHCNRF_SOURCE.SHCNRF_NewDelivery,
				(int)ChangeMask, _notificationMessage, 1, in entry);

			if (registrationId == 0)
			{
				PInvoke.CoTaskMemFree(nativePidl);
				if (_registrations.Count is 0)
				{
					DestroyNotificationWindow();
				}

				throw new InvalidOperationException("SHChangeNotifyRegister failed.");
			}

			var registration = new WindowsShellFolderChangeRegistration(folderPidl, nativePidl, registrationId, recursive);
			_registrations.Add(registration);

			return registration.CreateSubscription(this);
		}, cancellationToken);
	}

	private async Task UnsubscribeAsync(WindowsShellFolderChangeRegistration registration, WindowsShellChangeSubscription subscription)
	{
		subscription.Changes.Writer.TryComplete();

		if (Volatile.Read(ref _isDisposed) != 0)
		{
			return;
		}

		try
		{
			await _scheduler.InvokeAsync(() =>
			{
				if (!registration.Subscriptions.Remove(subscription))
				{
					return true;
				}

				if (registration.Subscriptions.Count is not 0 || !_registrations.Remove(registration))
				{
					return true;
				}

				if (registration.RegistrationId != 0)
				{
					PInvoke.SHChangeNotifyDeregister(registration.RegistrationId);
				}

				unsafe
				{
					PInvoke.CoTaskMemFree(registration.NativePidl);
				}

				if (_registrations.Count is 0)
				{
					DestroyNotificationWindow();
				}

				return true;
			}).ConfigureAwait(false);
		}
		catch (ObjectDisposedException)
		{
		}
	}

	private WindowsShellFolderChangeRegistration? FindRegistration(ReadOnlyMemory<byte> folderPidl, bool recursive)
	{
		foreach (var registration in _registrations)
		{
			if (registration.Recursive == recursive && WindowShellHelpers.AreSamePidl(folderPidl, registration.FolderPidl))
			{
				return registration;
			}
		}

		return null;
	}

	private unsafe LRESULT WindowProc(HWND hWnd, uint message, WPARAM wParam, LPARAM lParam)
	{
		if (message == _notificationMessage)
		{
			try
			{
				ITEMIDLIST** pidls = null;
				var eventId = 0;
				HANDLE notificationHandle = (HANDLE)(nint)wParam.Value;
				var lockHandle = PInvoke.SHChangeNotification_Lock(notificationHandle, unchecked((uint)lParam.Value), &pidls, &eventId);

				if (lockHandle.IsNull)
				{
					var refresh = new WindowsShellChange(SHCNE_ID.SHCNE_UPDATEDIR, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);
					foreach (var registration in _registrations)
					{
						foreach (var subscription in registration.Subscriptions)
						{
							subscription.Publish(refresh);
						}
					}

					return default;
				}

				WindowsShellChange change;
				try
				{
					var firstPidl = pidls is null ? ReadOnlyMemory<byte>.Empty : WindowShellHelpers.CopyPidl(pidls[0]);
					var secondPidl = pidls is null ? ReadOnlyMemory<byte>.Empty : WindowShellHelpers.CopyPidl(pidls[1]);
					change = new WindowsShellChange((SHCNE_ID)eventId, firstPidl, secondPidl);
				}
				finally
				{
					PInvoke.SHChangeNotification_Unlock(lockHandle);
				}

				foreach (var registration in _registrations)
				{
					if ((change.FirstAbsolutePidl.IsEmpty && change.SecondAbsolutePidl.IsEmpty)
						|| WindowShellHelpers.MatchesPidl(change.FirstAbsolutePidl, registration.FolderPidl, registration.Recursive)
						|| WindowShellHelpers.IsInFolder(change.FirstAbsolutePidl, registration.FolderPidl, registration.Recursive)
						|| WindowShellHelpers.MatchesPidl(change.SecondAbsolutePidl, registration.FolderPidl, registration.Recursive)
						|| WindowShellHelpers.IsInFolder(change.SecondAbsolutePidl, registration.FolderPidl, registration.Recursive))
					{
						foreach (var subscription in registration.Subscriptions)
						{
							subscription.Publish(change);
						}
					}
				}
			}
			catch (Exception error)
			{
				foreach (var registration in _registrations)
				{
					if (registration.RegistrationId != 0)
					{
						PInvoke.SHChangeNotifyDeregister(registration.RegistrationId);
					}

					PInvoke.CoTaskMemFree(registration.NativePidl);
					foreach (var subscription in registration.Subscriptions)
					{
						subscription.Changes.Writer.TryComplete(error);
					}
				}

				_registrations.Clear();
				DestroyNotificationWindow();
			}

			return default;
		}

		return PInvoke.DefWindowProc(hWnd, message, wParam, lParam);
	}

	private unsafe void DestroyNotificationWindow()
	{
		if (_window.IsNull)
		{
			return;
		}

		PInvoke.DestroyWindow(_window);
		_window = HWND.Null;

		PInvoke.UnregisterClass(_windowClassName, PInvoke.GetModuleHandle(string.Empty));
		_wndProc = null;
		_notificationMessage = 0;
	}

	public ValueTask DisposeAsync()
	{
		lock (_disposalLock)
		{
			_disposeTask ??= DisposeWatcherAsync();

			return new ValueTask(_disposeTask);
		}

		async Task DisposeWatcherAsync()
		{
			Interlocked.Exchange(ref _isDisposed, 1);

			try
			{
				await _scheduler.InvokeAsync(() =>
				{
					unsafe
					{
						foreach (var registration in _registrations)
						{
							if (registration.RegistrationId != 0)
							{
								PInvoke.SHChangeNotifyDeregister(registration.RegistrationId);
							}

							PInvoke.CoTaskMemFree(registration.NativePidl);
							foreach (var subscription in registration.Subscriptions)
							{
								subscription.Changes.Writer.TryComplete();
							}
						}
					}

					_registrations.Clear();
					DestroyNotificationWindow();

					return true;
				}).ConfigureAwait(false);
			}
			catch (Exception error)
			{
				foreach (var registration in _registrations)
				{
					foreach (var subscription in registration.Subscriptions)
					{
						subscription.Changes.Writer.TryComplete(error);
					}
				}

				throw;
			}
			finally
			{
				foreach (var registration in _registrations)
				{
					foreach (var subscription in registration.Subscriptions)
					{
						subscription.Changes.Writer.TryComplete();
					}
				}
			}
		}
	}

	private sealed unsafe class WindowsShellFolderChangeRegistration
	{
		internal ReadOnlyMemory<byte> FolderPidl { get; }

		internal ITEMIDLIST* NativePidl { get; }

		internal uint RegistrationId { get; }

		internal bool Recursive { get; }

		internal List<WindowsShellChangeSubscription> Subscriptions { get; } = [];

		internal WindowsShellFolderChangeRegistration(ReadOnlyMemory<byte> folderPidl, ITEMIDLIST* nativePidl, uint registrationId, bool recursive)
		{
			FolderPidl = folderPidl;
			NativePidl = nativePidl;
			RegistrationId = registrationId;
			Recursive = recursive;
		}

		internal WindowsShellChangeSubscription CreateSubscription(WindowsShellChangeWatcher watcher)
		{
			var subscription = new WindowsShellChangeSubscription(FolderPidl, current => watcher.UnsubscribeAsync(this, current));
			Subscriptions.Add(subscription);

			return subscription;
		}
	}

	internal sealed class WindowsShellChangeSubscription : IAsyncDisposable
	{
		private readonly ReadOnlyMemory<byte> _folderPidl;
		private readonly Func<WindowsShellChangeSubscription, Task> _unsubscribeAsync;
		private int _isDisposed;

		internal Channel<WindowsShellChange> Changes { get; } = Channel.CreateBounded<WindowsShellChange>(
			new BoundedChannelOptions(SubscriptionCapacity)
			{
				FullMode = BoundedChannelFullMode.Wait,
				SingleReader = false,
				SingleWriter = true,
				AllowSynchronousContinuations = false
			});

		internal WindowsShellChangeSubscription(ReadOnlyMemory<byte> folderPidl, Func<WindowsShellChangeSubscription, Task> unsubscribeAsync)
		{
			_folderPidl = folderPidl;
			_unsubscribeAsync = unsubscribeAsync;
		}

		internal void Publish(WindowsShellChange change)
		{
			if (Changes.Writer.TryWrite(change))
			{
				return;
			}

			while (Changes.Reader.TryRead(out _))
			{
			}

			Changes.Writer.TryWrite(new WindowsShellChange(SHCNE_ID.SHCNE_UPDATEDIR, _folderPidl, ReadOnlyMemory<byte>.Empty));
		}

		internal ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
		{
			return Changes.Reader.WaitToReadAsync(cancellationToken);
		}

		internal bool TryRead(out WindowsShellChange change)
		{
			return Changes.Reader.TryRead(out change!);
		}

		/// <inheritdoc/>
		public async ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
			{
				await _unsubscribeAsync(this).ConfigureAwait(false);
			}
		}
	}
}

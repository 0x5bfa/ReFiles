// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using System.Threading.Channels;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.WindowsAndMessaging;
using WNDPROC = Windows.Win32.Extras.ManagedWNDPROC;

namespace Files.Core.Storage.Windows;

internal sealed record WindowsShellChange(SHCNE_ID EventId, ReadOnlyMemory<byte> FirstAbsolutePidl, ReadOnlyMemory<byte> SecondAbsolutePidl);

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

	private readonly IWindowsShellScheduler scheduler;
	private readonly string windowClassName = $"{WindowClassPrefix}_{Guid.NewGuid():N}";
	private readonly List<Registration> registrations = [];
	private readonly object disposalLock = new();
	private WNDPROC? wndProc;
	private HWND window;
	private uint notificationMessage;
	private Task? disposeTask;
	private int isDisposed;

	public WindowsShellChangeWatcher(IWindowsShellScheduler scheduler)
	{
		ArgumentNullException.ThrowIfNull(scheduler);
		this.scheduler = scheduler;
	}

	public Task<WindowsShellChangeSubscription> SubscribeAsync(
		WindowsItemLocator folderLocator,
		bool recursive,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(folderLocator);

		return scheduler.InvokeAsync(() => SubscribeCore(folderLocator, recursive), cancellationToken);
	}

	public ValueTask DisposeAsync()
	{
		lock (disposalLock)
		{
			disposeTask ??= DisposeWatcherAsync();
			return new ValueTask(disposeTask);
		}
	}

	private async Task DisposeWatcherAsync()
	{
		Interlocked.Exchange(ref isDisposed, 1);

		try
		{
			await scheduler
				.InvokeAsync(() => {DisposeCore(); return true;})
				.ConfigureAwait(false);
		}
		catch (Exception error)
		{
			foreach (var registration in registrations)
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
			foreach (var registration in registrations)
			{
				foreach (var subscription in registration.Subscriptions)
				{
					subscription.Changes.Writer.TryComplete();
				}
			}
		}
	}

	private async Task UnsubscribeAsync(Registration registration, WindowsShellChangeSubscription subscription)
	{
		subscription.Changes.Writer.TryComplete();

		if (Volatile.Read(ref isDisposed) != 0)
		{
			return;
		}

		try
		{
			await scheduler.InvokeAsync(() => {RemoveCore(registration, subscription); return true;})
				.ConfigureAwait(false);
		}
		catch (ObjectDisposedException)
		{
		}
	}

	private unsafe WindowsShellChangeSubscription SubscribeCore(WindowsItemLocator folderLocator, bool recursive)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) != 0, this);

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

		CreateNotificationWindow();

		var nativePidl = CopyRegistrationPidl(folderPidl);
		var entry = new SHChangeNotifyEntry
		{
			pidl = nativePidl,
			fRecursive = recursive,
		};

		var registrationId = PInvoke.SHChangeNotifyRegister(
			window,
			SHCNRF_SOURCE.SHCNRF_ShellLevel
				| SHCNRF_SOURCE.SHCNRF_InterruptLevel
				| SHCNRF_SOURCE.SHCNRF_NewDelivery,
			(int)ChangeMask,
			notificationMessage,
			1,
			in entry);

		if (registrationId == 0)
		{
			PInvoke.CoTaskMemFree(nativePidl);
			if (registrations.Count is 0)
			{
				DestroyNotificationWindow();
			}

			throw new InvalidOperationException("SHChangeNotifyRegister failed.");
		}

		var registration = new Registration(folderPidl, nativePidl, registrationId, recursive);
		registrations.Add(registration);
		return registration.CreateSubscription(this);
	}

	private Registration? FindRegistration(ReadOnlyMemory<byte> folderPidl, bool recursive)
	{
		foreach (var registration in registrations)
		{
			if (registration.Recursive == recursive
				&& WindowsShellItemResolver.AreSamePidlOnCurrentSta(folderPidl, registration.FolderPidl))
			{
				return registration;
			}
		}

		return null;
	}

	private unsafe void CreateNotificationWindow()
	{
		if (!window.IsNull)
		{
			return;
		}

		fixed (char* className = windowClassName)
		{
			wndProc ??= new(WindowProc);

			var windowClass = new WNDCLASSEXW
			{
				cbSize = (uint)sizeof(WNDCLASSEXW),
				hInstance = PInvoke.GetModuleHandle(default(PCWSTR)),
				lpszClassName = className,
				lpfnWndProc =
					(delegate* unmanaged[Stdcall]<
						HWND,
						uint,
						WPARAM,
						LPARAM,
						LRESULT>)Marshal.GetFunctionPointerForDelegate(wndProc),
			};

			if (PInvoke.RegisterClassEx(&windowClass) == 0)
			{
				throw new InvalidOperationException("RegisterClassEx failed.");
			}

			notificationMessage = PInvoke.RegisterWindowMessage(windowClassName);
			if (notificationMessage == 0)
			{
				PInvoke.UnregisterClass(className, windowClass.hInstance);
				throw new InvalidOperationException("RegisterWindowMessage failed.");
			}

			window = PInvoke.CreateWindowEx(
				WINDOW_EX_STYLE.WS_EX_LEFT,
				className,
				default,
				WINDOW_STYLE.WS_OVERLAPPED,
				0,
				0,
				1,
				1,
				HWND.Null,
				HMENU.Null,
				HINSTANCE.Null,
				null);

			if (window.IsNull)
			{
				PInvoke.UnregisterClass(className, windowClass.hInstance);
				throw new InvalidOperationException("CreateWindowEx failed.");
			}
		}
	}

	private LRESULT WindowProc(HWND hWnd, uint message, WPARAM wParam, LPARAM lParam)
	{
		if (message == notificationMessage)
		{
			try
			{
				ProcessNotification(wParam, lParam);
			}
			catch (Exception error)
			{
				FailCore(error);
			}

			return default;
		}

		return PInvoke.DefWindowProc(hWnd, message, wParam, lParam);
	}

	private unsafe void ProcessNotification(WPARAM wParam, LPARAM lParam)
	{
		ITEMIDLIST** pidls = null;
		var eventId = 0;
		var lockHandle = PInvoke.SHChangeNotification_Lock(new HANDLE((nint)wParam.Value), unchecked((uint)lParam.Value), &pidls, &eventId);

		if (lockHandle.IsNull)
		{
			PublishDirectoryRefresh();
			return;
		}

		WindowsShellChange change;

		try
		{
			change = new WindowsShellChange(
				(SHCNE_ID)eventId,
				pidls is null ? ReadOnlyMemory<byte>.Empty : CopyPidl(pidls[0]),
				pidls is null ? ReadOnlyMemory<byte>.Empty : CopyPidl(pidls[1]));
		}
		finally
		{
			PInvoke.SHChangeNotification_Unlock(lockHandle);
		}

		foreach (var registration in registrations)
		{
			if (MatchesRegistration(change, registration))
			{
				foreach (var subscription in registration.Subscriptions)
				{
					subscription.Publish(change);
				}
			}
		}
	}

	private void PublishDirectoryRefresh()
	{
		var refresh = new WindowsShellChange(SHCNE_ID.SHCNE_UPDATEDIR, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);

		foreach (var registration in registrations)
		{
			foreach (var subscription in registration.Subscriptions)
			{
				subscription.Publish(refresh);
			}
		}
	}

	private bool MatchesRegistration(WindowsShellChange change, Registration registration)
	{
		if (change.FirstAbsolutePidl.IsEmpty
			&& change.SecondAbsolutePidl.IsEmpty)
		{
			return true;
		}

		return MatchesPidl(change.FirstAbsolutePidl, registration.FolderPidl, registration.Recursive)
			|| WindowsShellItemResolver.IsInFolderOnCurrentSta(change.FirstAbsolutePidl, registration.FolderPidl, registration.Recursive)
			|| MatchesPidl(change.SecondAbsolutePidl, registration.FolderPidl, registration.Recursive)
			|| WindowsShellItemResolver.IsInFolderOnCurrentSta(change.SecondAbsolutePidl, registration.FolderPidl, registration.Recursive);
	}

	private static bool MatchesPidl(ReadOnlyMemory<byte> itemPidl, ReadOnlyMemory<byte> folderPidl, bool recursive)
	{
		if (itemPidl.IsEmpty || folderPidl.IsEmpty)
		{
			return false;
		}

		var item = itemPidl.Span;
		var folder = folderPidl.Span;

		if (item.SequenceEqual(folder))
		{
			return true;
		}

		if (folder.Length < sizeof(ushort)
			|| item.Length <= folder.Length
			|| !item[..^sizeof(ushort)].StartsWith(folder[..^sizeof(ushort)]))
		{
			return false;
		}

		if (recursive)
		{
			return true;
		}

		var childOffset = folder.Length - sizeof(ushort);
		var childSize = BitConverter.ToUInt16(item[childOffset..]);
		return childSize >= sizeof(ushort)
			&& childSize + sizeof(ushort) == item.Length - childOffset;
	}

	private static unsafe ITEMIDLIST* CopyRegistrationPidl(ReadOnlySpan<byte> source)
	{
		var nativePidl = (ITEMIDLIST*)Marshal.AllocCoTaskMem(source.Length);
		source.CopyTo(new Span<byte>((byte*)nativePidl, source.Length));
		return nativePidl;
	}

	private static unsafe ReadOnlyMemory<byte> CopyPidl(ITEMIDLIST* pidl)
	{
		if (pidl is null)
		{
			return ReadOnlyMemory<byte>.Empty;
		}

		var size = GetPidlSize(pidl);
		if (size is 0)
		{
			return ReadOnlyMemory<byte>.Empty;
		}

		var bytes = GC.AllocateUninitializedArray<byte>(size);
		Marshal.Copy((nint)pidl, bytes, 0, size);
		return bytes;
	}

	private static unsafe int GetPidlSize(ITEMIDLIST* pidl)
	{
		var offset = 0;

		while (offset <= int.MaxValue - sizeof(ushort))
		{
			var itemSize = *(ushort*)((byte*)pidl + offset);
			if (itemSize is 0)
			{
				return offset + sizeof(ushort);
			}

			if (itemSize < sizeof(ushort)
				|| offset > int.MaxValue - itemSize)
			{
				return 0;
			}

			offset += itemSize;
		}

		return 0;
	}

	private unsafe void RemoveCore(Registration registration, WindowsShellChangeSubscription subscription)
	{
		if (!registration.Subscriptions.Remove(subscription))
		{
			return;
		}

		if (registration.Subscriptions.Count is not 0
			|| !registrations.Remove(registration))
		{
			return;
		}

		if (registration.RegistrationId != 0)
		{
			PInvoke.SHChangeNotifyDeregister(registration.RegistrationId);
		}

		PInvoke.CoTaskMemFree(registration.NativePidl);

		if (registrations.Count is 0)
		{
			DestroyNotificationWindow();
		}
	}

	private unsafe void DisposeCore()
	{
		foreach (var registration in registrations)
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

		registrations.Clear();
		DestroyNotificationWindow();
	}

	private unsafe void DestroyNotificationWindow()
	{
		if (window.IsNull)
		{
			return;
		}

		PInvoke.DestroyWindow(window);
		window = HWND.Null;

		fixed (char* className = windowClassName)
		{
			PInvoke.UnregisterClass(className, PInvoke.GetModuleHandle(default(PCWSTR)));
		}

		wndProc = null;
		notificationMessage = 0;
	}

	private unsafe void FailCore(Exception error)
	{
		foreach (var registration in registrations)
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

		registrations.Clear();
		DestroyNotificationWindow();
	}

	internal sealed unsafe class Registration
	{
		public Registration(ReadOnlyMemory<byte> folderPidl, ITEMIDLIST* nativePidl, uint registrationId, bool recursive)
		{
			FolderPidl = folderPidl;
			NativePidl = nativePidl;
			RegistrationId = registrationId;
			Recursive = recursive;
		}

		public ReadOnlyMemory<byte> FolderPidl { get; }

		public ITEMIDLIST* NativePidl { get; }

		public uint RegistrationId { get; }

		public bool Recursive { get; }

		public List<WindowsShellChangeSubscription> Subscriptions { get; } = [];

		public WindowsShellChangeSubscription CreateSubscription(WindowsShellChangeWatcher watcher)
		{
			var subscription = new WindowsShellChangeSubscription(watcher, this);
			Subscriptions.Add(subscription);
			return subscription;
		}
	}

	internal sealed class WindowsShellChangeSubscription : IAsyncDisposable
	{
		private readonly WindowsShellChangeWatcher watcher;
		private readonly Registration registration;
		private int isDisposed;

		internal WindowsShellChangeSubscription(WindowsShellChangeWatcher watcher, Registration registration)
		{
			this.watcher = watcher;
			this.registration = registration;
		}

		public Channel<WindowsShellChange> Changes { get; } =
			Channel.CreateBounded<WindowsShellChange>(
				new BoundedChannelOptions(SubscriptionCapacity)
				{
					FullMode = BoundedChannelFullMode.Wait,
					SingleReader = false,
					SingleWriter = true,
					AllowSynchronousContinuations = false,
				});

		public void Publish(WindowsShellChange change)
		{
			if (Changes.Writer.TryWrite(change))
			{
				return;
			}

			while (Changes.Reader.TryRead(out _))
			{
			}

			Changes.Writer.TryWrite(new WindowsShellChange(SHCNE_ID.SHCNE_UPDATEDIR, registration.FolderPidl, ReadOnlyMemory<byte>.Empty));
		}

		public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
		{
			return Changes.Reader.WaitToReadAsync(cancellationToken);
		}

		public bool TryRead(out WindowsShellChange change)
		{
			return Changes.Reader.TryRead(out change!);
		}

		public async ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref isDisposed, 1) == 0)
			{
				await watcher
					.UnsubscribeAsync(registration, this)
					.ConfigureAwait(false);
			}
		}
	}
}

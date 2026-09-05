// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Diagnostics;
using Files.Core.Capabilities.Changes;
using Files.Core.Storage;
using Windows.Win32.UI.Shell;

namespace Files.Core.Windows;

internal sealed class WindowsFolderChangeSource : IFolderChangeSource
{
	private readonly WindowsStorageSource _source;

	private readonly WindowsItemLocator _folderLocator;

	private readonly CancellationTokenSource _lifetime = new();

	private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

	private readonly Lock _disposeSync = new();

	private WindowsShellChangeWatcher.WindowsShellChangeSubscription? _subscription;

	private Task? _pumpTask;

	private Task? _disposeTask;

	private int _isStarted;

	private int _isDisposed;

	public event EventHandler<FolderChangeEventArgs>? Changed;

	public event EventHandler<FolderChangeErrorEventArgs>? Faulted;

	public WindowsFolderChangeSource(WindowsStorageSource source, WindowsItemLocator folderLocator)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(folderLocator);

		_source = source;
		_folderLocator = folderLocator;
	}

	public async ValueTask StartAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

		await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

			if (Volatile.Read(ref _isStarted) != 0)
			{
				return;
			}

			using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
			var newSubscription = await _source.ChangeWatcher.SubscribeAsync(_folderLocator, recursive: false, linkedCancellation.Token).ConfigureAwait(false);

			try
			{
				ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

				_subscription = newSubscription;
				Volatile.Write(ref _isStarted, 1);
				_pumpTask = PumpAsync(newSubscription, _lifetime.Token);
			}
			catch (Exception startError)
			{
				try
				{
					await newSubscription.DisposeAsync().ConfigureAwait(false);
				}
				catch (Exception cleanupError)
				{
					throw new AggregateException("Folder watcher startup and cleanup failed.", startError, cleanupError);
				}

				throw;
			}
		}
		finally
		{
			_lifecycleGate.Release();
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
		{
			return;
		}

		_lifetime.Cancel();
		_ = ObserveDisposeAsync(GetDisposeTask());
		GC.SuppressFinalize(this);
	}

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
		{
			_lifetime.Cancel();
		}

		return new ValueTask(GetDisposeTask());
	}

	private async Task PumpAsync(WindowsShellChangeWatcher.WindowsShellChangeSubscription changeSubscription, CancellationToken cancellationToken)
	{
		try
		{
			while (await changeSubscription .WaitToReadAsync(cancellationToken) .ConfigureAwait(false))
			{
				while (changeSubscription.TryRead(out var change))
				{
					var converted = await ConvertAsync(change, cancellationToken).ConfigureAwait(false);
					Publish(converted);
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception error)
		{
			PublishFault(error);
		}
	}

	private async Task<FolderChange> ConvertAsync(WindowsShellChange change, CancellationToken cancellationToken)
	{
		var kind = GetKind(change.EventId);
		WindowsStorable? first = null;
		WindowsStorable? second = null;

		if (kind is FolderChangeKind.Renamed)
		{
			first = await _source.TryCreateFromAbsolutePidlAsync(change.FirstAbsolutePidl, cancellationToken).ConfigureAwait(false);
			second = await _source.TryCreateFromAbsolutePidlAsync(change.SecondAbsolutePidl, cancellationToken).ConfigureAwait(false);
		}
		else if (kind is not FolderChangeKind.DirectoryUpdated)
		{
			first = await _source.TryCreateFromAbsolutePidlAsync(change.FirstAbsolutePidl, cancellationToken).ConfigureAwait(false);
		}

		var currentItem = kind is FolderChangeKind.Deleted
			? null
			: CreateReference(second ?? first);
		var previousItem = kind is FolderChangeKind.Deleted or FolderChangeKind.Renamed
			? CreateReference(first)
			: null;

		return new FolderChange(kind, currentItem, previousItem, kind is FolderChangeKind.DirectoryUpdated || (kind is FolderChangeKind.Renamed ? first is null || second is null : first is null));
	}

	private void Publish(FolderChange change)
	{
		var handlers = Changed;
		if (handlers is null)
		{
			return;
		}

		var args = new FolderChangeEventArgs(change);
		foreach (EventHandler<FolderChangeEventArgs> handler in handlers.GetInvocationList())
		{
			try
			{
				handler(this, args);
			}
			catch (Exception error)
			{
				Trace.TraceError(error.ToString());
			}
		}
	}

	private void PublishFault(Exception error)
	{
		var handlers = Faulted;
		if (handlers is null)
		{
			Trace.TraceError(error.ToString());

			return;
		}

		var args = new FolderChangeErrorEventArgs(error);
		foreach (EventHandler<FolderChangeErrorEventArgs> handler in handlers.GetInvocationList())
		{
			try
			{
				handler(this, args);
			}
			catch (Exception handlerError)
			{
				Trace.TraceError(handlerError.ToString());
			}
		}
	}

	private StorableReference? CreateReference(WindowsStorable? storable)
	{
		return storable is null
			? null
			: new StorableReference(_source.SourceId, storable.Id, storable.Address);
	}

	private static FolderChangeKind GetKind(SHCNE_ID eventId)
	{
		if ((eventId & (SHCNE_ID.SHCNE_RENAMEITEM | SHCNE_ID.SHCNE_RENAMEFOLDER)) != 0)
		{
			return FolderChangeKind.Renamed;
		}

		if ((eventId & (SHCNE_ID.SHCNE_CREATE | SHCNE_ID.SHCNE_MKDIR)) != 0)
		{
			return FolderChangeKind.Created;
		}

		if ((eventId & (SHCNE_ID.SHCNE_DELETE | SHCNE_ID.SHCNE_RMDIR)) != 0)
		{
			return FolderChangeKind.Deleted;
		}

		if ((eventId & SHCNE_ID.SHCNE_UPDATEDIR) != 0)
		{
			return FolderChangeKind.DirectoryUpdated;
		}

		return FolderChangeKind.Updated;
	}

	private async Task DisposeAsyncCore()
	{
		WindowsShellChangeWatcher.WindowsShellChangeSubscription? currentSubscription;
		Task? currentPump;
		var errors = new List<Exception>();

		await _lifecycleGate.WaitAsync().ConfigureAwait(false);
		try
		{
			currentSubscription = _subscription;
			currentPump = _pumpTask;
			_subscription = null;
			_pumpTask = null;
		}
		finally
		{
			_lifecycleGate.Release();
		}

		if (currentPump is not null)
		{
			try
			{
				await currentPump.ConfigureAwait(false);
			}
			catch (Exception error)
			{
				errors.Add(error);
			}
		}

		if (currentSubscription is not null)
		{
			try
			{
				await currentSubscription.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception error)
			{
				errors.Add(error);
			}
		}

		_lifetime.Dispose();
		GC.SuppressFinalize(this);

		if (errors.Count is 1)
		{
			throw errors[0];
		}

		if (errors.Count > 1)
		{
			throw new AggregateException("Folder watcher cleanup failed.", errors);
		}
	}

	private Task GetDisposeTask()
	{
		lock (_disposeSync)
		{
			return _disposeTask ??= DisposeAsyncCore();
		}
	}

	private static async Task ObserveDisposeAsync(Task task)
	{
		try
		{
			await task.ConfigureAwait(false);
		}
		catch (Exception error)
		{
			Trace.TraceError(error.ToString());
		}
	}
}

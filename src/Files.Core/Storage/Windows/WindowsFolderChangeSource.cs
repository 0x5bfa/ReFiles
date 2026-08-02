// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using Files.Core.ItemFeatures.Changes;
using Files.Core.Storage;
using Windows.Win32.UI.Shell;

namespace Files.Core.Storage.Windows;

internal sealed class WindowsFolderChangeSource : IFolderChangeSource
{
	private readonly WindowsStorageSource source;
	private readonly WindowsItemLocator folderLocator;
	private readonly CancellationTokenSource lifetime = new();
	private readonly SemaphoreSlim lifecycleGate = new(1, 1);
	private readonly object disposeSync = new();
	private WindowsShellChangeWatcher.WindowsShellChangeSubscription? subscription;
	private Task? pumpTask;
	private Task? disposeTask;
	private int isStarted;
	private int isDisposed;

	public WindowsFolderChangeSource(WindowsStorageSource source, WindowsItemLocator folderLocator)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(folderLocator);

		this.source = source;
		this.folderLocator = folderLocator;
	}

	public event EventHandler<FolderChangeEventArgs>? Changed;

	public event EventHandler<FolderChangeErrorEventArgs>? Faulted;

	public async ValueTask StartAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) != 0, this);

		await lifecycleGate
			.WaitAsync(cancellationToken)
			.ConfigureAwait(false);

		try
		{
			ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) != 0, this);

			if (Volatile.Read(ref isStarted) != 0)
			{
				return;
			}

			using var linkedCancellation =
				CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
			var newSubscription = await source.ChangeWatcher
				.SubscribeAsync(folderLocator, recursive: false, linkedCancellation.Token)
				.ConfigureAwait(false);

			try
			{
				ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) != 0, this);
				subscription = newSubscription;
				Volatile.Write(ref isStarted, 1);
				pumpTask = PumpAsync(newSubscription, lifetime.Token);
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
			lifecycleGate.Release();
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref isDisposed, 1) != 0)
		{
			return;
		}

		lifetime.Cancel();
		_ = ObserveDisposeAsync(GetDisposeTask());
		GC.SuppressFinalize(this);
	}

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref isDisposed, 1) == 0)
		{
			lifetime.Cancel();
		}

		return new ValueTask(GetDisposeTask());
	}

	private async Task PumpAsync(WindowsShellChangeWatcher.WindowsShellChangeSubscription changeSubscription, CancellationToken cancellationToken)
	{
		try
		{
			while (await changeSubscription
				.WaitToReadAsync(cancellationToken)
				.ConfigureAwait(false))
			{
				while (changeSubscription.TryRead(out var change))
				{
					var converted = await ConvertAsync(change, cancellationToken)
						.ConfigureAwait(false);
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
			first = await source
				.TryCreateFromAbsolutePidlAsync(change.FirstAbsolutePidl, cancellationToken)
				.ConfigureAwait(false);
			second = await source
				.TryCreateFromAbsolutePidlAsync(change.SecondAbsolutePidl, cancellationToken)
				.ConfigureAwait(false);
		}
		else if (kind is not FolderChangeKind.DirectoryUpdated)
		{
			first = await source
				.TryCreateFromAbsolutePidlAsync(change.FirstAbsolutePidl, cancellationToken)
				.ConfigureAwait(false);
		}

		var currentItem = kind is FolderChangeKind.Deleted
			? null
			: CreateReference(second ?? first);
		var previousItem = kind is FolderChangeKind.Deleted or FolderChangeKind.Renamed
			? CreateReference(first)
			: null;

		return new FolderChange(
			kind,
			currentItem,
			previousItem,
			kind is FolderChangeKind.DirectoryUpdated
				|| (kind is FolderChangeKind.Renamed
					? first is null || second is null
					: first is null));
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
			: new StorableReference(source.SourceId, storable.Id, storable.Address);
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

		await lifecycleGate.WaitAsync().ConfigureAwait(false);
		try
		{
			currentSubscription = subscription;
			currentPump = pumpTask;
			subscription = null;
			pumpTask = null;
		}
		finally
		{
			lifecycleGate.Release();
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

		lifetime.Dispose();
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
		lock (disposeSync)
		{
			return disposeTask ??= DisposeAsyncCore();
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

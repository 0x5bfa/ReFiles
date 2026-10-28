// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Files.Core.Storage;
using Files.Core.Storage.Ftp;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Storage.Windows;
using Files.Infrastructure;
using Files.ItemProperties;

namespace Files.StorageOperations;

internal sealed partial class WinRTStorageOperationService : IStorageOperationService, IStorageJobService, IWindowsShellOperationService, IOperationStatusSource, IThumbnailCache, IDisposable
{
	private readonly Lock _syncRoot = new();
	private readonly HashSet<string> _ftpSources = [];
	private readonly CancellationTokenSource _shutdown = new();
	private readonly MemoryThumbnailCache _fallbackThumbnailCache = new();
	private readonly Task _observer;
	private Task<BackgroundServer.AppService>? _connection;
	private BackgroundServer.OperationSummary[] _operations = [];
	private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private Guid _serverInstanceId;
	private ulong _revision;
	private Exception? _connectionError;
	private bool _disposed;

	public event EventHandler? Changed;

	public IStorageOperationMonitor Monitor => this;

	public IStorageJobService Jobs => this;

	internal WinRTStorageOperationService()
	{
		_observer = Task.Run(ObserveAsync);
	}

	public bool CanHandle(StorageOperationRequest request)
	{
		return request switch
		{
			RenameOperationRequest rename => IsWindowsReference(rename.Item, true),
			CreateItemOperationRequest create => IsWindowsReference(create.Parent, true),
			CopyOperationRequest copy => copy.Source is ConfiguredItemLocator or ArchiveRecordLocator && IsTransferSource(copy.Source) && IsWindowsReference(copy.DestinationFolder, true),
			MoveOperationRequest { Source: ConfiguredItemLocator source } move => IsWindowsReference(source.Reference, true) && IsWindowsReference(move.DestinationFolder, true),
			DeleteOperationRequest delete => IsWindowsReference(delete.Item, false),
			_ => false,
		};
	}

	internal async Task RegisterFtpConnectionAsync(FtpConnectionProfile profile, FtpCredential? credential = null, CancellationToken cancellationToken = default)
	{
		var server = await GetConnection().WaitAsync(cancellationToken).ConfigureAwait(false);
		var data = new BackgroundServer.FtpConnectionData { ConnectionId = profile.ConnectionId, DisplayName = profile.DisplayName, Host = profile.Host, Port = profile.Port,
			SecurityMode = (int)profile.SecurityMode, RootPath = profile.RootPath.Value, UserNameHint = profile.UserNameHint ?? string.Empty, PathComparison = (int)profile.PathComparison };
		var sourceId = server.RegisterFtpConnection(data, credential?.UserName ?? "anonymous", credential?.Password ?? "anonymous@");
		lock (_syncRoot)
		{
			_ftpSources.Add(sourceId);
		}
	}

	public ValueTask<StorageOperationResult> ExecuteAsync(StorageOperationRequest request, IProgress<StorageOperationProgress>? progress = null, CancellationToken cancellationToken = default,
		IStorageOperationControl? operationControl = null)
	{
		throw new NotSupportedException("Use Jobs.SubmitBatchAsync and Jobs.WaitForCompletionAsync for independent server operations.");
	}

	internal Task<BackgroundServer.AppService> ConnectAsync()
	{
		return GetConnection();
	}

	internal async Task RegisterWindowAsync(nint windowHandle, CancellationToken cancellationToken = default)
	{
		var server = await GetConnection().WaitAsync(cancellationToken).ConfigureAwait(false);
		server.RegisterWindow(windowHandle.ToInt64());
	}

	internal void SetActiveWindow(nint windowHandle)
	{
		_ = SetActiveWindowAsync(windowHandle);
	}

	internal async Task UnregisterWindowAsync(nint windowHandle, CancellationToken cancellationToken = default)
	{
		var server = await GetConnection().WaitAsync(cancellationToken).ConfigureAwait(false);
		server.UnregisterWindow(windowHandle.ToInt64());
	}

	public async Task<IReadOnlyList<WindowsShellNewItem>> GetShellNewItemsAsync(string folderPath, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

		var server = await GetConnection().WaitAsync(cancellationToken).ConfigureAwait(false);
		var items = await server.GetShellNewItemsAsync(folderPath).AsTask(cancellationToken).ConfigureAwait(false);

		return items.Select(static item => new WindowsShellNewItem(item.CommandOffset, item.Name,
			string.IsNullOrEmpty(item.IconDataBase64) ? ReadOnlyMemory<byte>.Empty : Convert.FromBase64String(item.IconDataBase64), item.IsEnabled)).ToArray();
	}

	public async ValueTask<ThumbnailCacheEntry?> GetAsync(ThumbnailCacheKey key, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(key);

		cancellationToken.ThrowIfCancellationRequested();

		try
		{
			var server = await GetConnection().WaitAsync(cancellationToken).ConfigureAwait(false);
			var data = server.GetThumbnailCacheEntry(ToData(key));
			await _fallbackThumbnailCache.InvalidateAsync(new StorableReference(key.SourceId, key.ItemId), cancellationToken).ConfigureAwait(false);

			return data.HasValue ? FromData(data) : null;
		}
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
		{
			Debug.WriteLine($"Files.BackgroundServer thumbnail cache read failed: {exception}");

			return await _fallbackThumbnailCache.GetAsync(key, cancellationToken).ConfigureAwait(false);
		}
	}

	public async ValueTask SetAsync(ThumbnailCacheKey key, ThumbnailCacheEntry entry, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(key);

		ArgumentNullException.ThrowIfNull(entry);

		cancellationToken.ThrowIfCancellationRequested();

		try
		{
			var server = await GetConnection().WaitAsync(cancellationToken).ConfigureAwait(false);
			server.SetThumbnailCacheEntry(ToData(key), ToData(entry));
			await _fallbackThumbnailCache.InvalidateAsync(new StorableReference(key.SourceId, key.ItemId), cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
		{
			Debug.WriteLine($"Files.BackgroundServer thumbnail cache write failed: {exception}");
			await _fallbackThumbnailCache.SetAsync(key, entry, cancellationToken).ConfigureAwait(false);
		}
	}

	public async ValueTask<long> GetInvalidationVersionAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(reference);

		cancellationToken.ThrowIfCancellationRequested();

		try
		{
			var server = await GetConnection().WaitAsync(cancellationToken).ConfigureAwait(false);

			return server.GetThumbnailInvalidationVersion(ToData(reference));
		}
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
		{
			Debug.WriteLine($"Files.BackgroundServer thumbnail cache version read failed: {exception}");

			return await _fallbackThumbnailCache.GetInvalidationVersionAsync(reference, cancellationToken).ConfigureAwait(false);
		}
	}

	public async ValueTask<bool> TrySetAsync(ThumbnailCacheKey key, ThumbnailCacheEntry entry, long expectedInvalidationVersion, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(key);

		ArgumentNullException.ThrowIfNull(entry);

		ArgumentOutOfRangeException.ThrowIfNegative(expectedInvalidationVersion);

		cancellationToken.ThrowIfCancellationRequested();

		try
		{
			var server = await GetConnection().WaitAsync(cancellationToken).ConfigureAwait(false);
			var stored = server.TrySetThumbnailCacheEntry(ToData(key), ToData(entry), expectedInvalidationVersion);
			await _fallbackThumbnailCache.InvalidateAsync(new StorableReference(key.SourceId, key.ItemId), cancellationToken).ConfigureAwait(false);

			return stored;
		}
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
		{
			Debug.WriteLine($"Files.BackgroundServer thumbnail cache write validation failed: {exception}");

			return await _fallbackThumbnailCache.TrySetAsync(key, entry, expectedInvalidationVersion, cancellationToken).ConfigureAwait(false);
		}
	}

	public async ValueTask InvalidateAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(reference);

		cancellationToken.ThrowIfCancellationRequested();
		await _fallbackThumbnailCache.InvalidateAsync(reference, cancellationToken).ConfigureAwait(false);

		try
		{
			var server = await GetConnection().WaitAsync(cancellationToken).ConfigureAwait(false);
			server.InvalidateThumbnails(ToData(reference));
		}
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
		{
			Debug.WriteLine($"Files.BackgroundServer thumbnail cache invalidation failed: {exception}");
		}
	}

	public IReadOnlyList<StorageOperationStatus> GetOperations()
	{
		lock (_syncRoot)
		{
			return _operations.Select(StorageOperationStatusMapper.Convert).ToArray();
		}
	}

	public ValueTask<StorageOperationResult> ExecuteBatchAsync(IReadOnlyList<StorageOperationRequest> requests, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException("Use Jobs.SubmitBatchAsync and Jobs.WaitForCompletionAsync for independent server operations.");
	}

	public async ValueTask<StorageJobHandle> SubmitBatchAsync(Guid id, IReadOnlyList<StorageOperationRequest> requests, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(requests);

		var snapshot = requests.ToArray();
		if (snapshot.Length is 0 || snapshot.Any(request => request is null || !CanHandle(request)))
		{
			throw new NotSupportedException("The job must contain a nonempty batch of supported operations.");
		}

		if (snapshot.All(request => request is CopyOperationRequest or MoveOperationRequest))
		{
			var transfer = StorageTransferDataMapper.ToData(snapshot);

			return await SubmitOperationAsync(id, (server, jobId) => server.SubmitStorageTransfers(jobId, transfer.Items, transfer.Steps), cancellationToken).ConfigureAwait(false);
		}

		var items = snapshot.Select(ToData).ToArray();

		return await SubmitOperationAsync(id, (server, jobId) => server.SubmitStorageOperations(jobId, items), cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask<StorageOperationResult> WaitForCompletionAsync(StorageJobHandle job, CancellationToken cancellationToken = default)
	{
		return ToResult(await WaitForSummaryAsync(job, cancellationToken).ConfigureAwait(false));
	}

	public async Task TransferPathsAsync(IReadOnlyList<string> paths, StorableReference destination, bool move, CancellationToken cancellationToken)
	{
		var items = paths.Select(path => new BackgroundServer.StorageOperationData
		{
			Kind = move ? BackgroundServer.StorageOperationKind.Move : BackgroundServer.StorageOperationKind.Copy,
			Item = new BackgroundServer.StorageReferenceData { SourceId = WindowsStorageSource.DefaultSourceType, ItemId = string.Empty, HasAddress = true,
				AddressScheme = WindowsStorageSource.FileAddressScheme, AddressValue = path },
			Destination = ToData(destination),
			Name = string.Empty,
			DisplayName = Path.GetFileName(path.TrimEnd('\\', '/')),
			ConflictBehavior = (int)StorageConflictBehavior.Prompt,
		}).ToArray();
		var summary = await SubmitAsync(items, cancellationToken).ConfigureAwait(false);
		var result = ToResult(summary);
		if (!result.Succeeded)
		{
			throw result.Error!;
		}
	}

	public bool Cancel(Guid id)
	{
		return QueueControl(server => server.CancelOperation(id));
	}

	public bool SetPaused(Guid id, bool paused)
	{
		return QueueControl(server => server.SetOperationPaused(id, paused));
	}

	public bool ResolveInterruption(Guid id, Guid interruptionId, StorageOperationInterruptionResponse response)
	{
		return QueueControl(server => server.ResolveOperationInterruption(id, interruptionId, (int)response.Decision, response.ApplyToAll));
	}

	public bool Forget(Guid id)
	{
		return QueueControl(server => server.ForgetOperation(id));
	}

	public async Task InvokeNewItemAsync(string folderPath, uint commandOffset, string name, CancellationToken cancellationToken)
	{
		var summary = await RunOperationAsync((server, id) => server.SubmitShellNewItem(id, folderPath, commandOffset, name), cancellationToken).ConfigureAwait(false);
		var result = ToResult(summary);
		if (!result.Succeeded)
		{
			throw result.Error!;
		}
	}

	internal async Task ApplyAttributesAsync(IReadOnlyList<string> paths, ItemPropertiesFileSystemChanges changes, CancellationToken cancellationToken)
	{
		var result = await ApplyPropertiesAsync(paths, changes, null, cancellationToken).ConfigureAwait(false);
		if (!result.Succeeded)
		{
			throw result.Error!;
		}
	}

	internal async Task<StorageOperationResult> ApplyPropertiesAsync(IReadOnlyList<string> paths, ItemPropertiesFileSystemChanges changes,
		RenameOperationRequest? rename, CancellationToken cancellationToken)
	{
		var indexed = changes.IsDrive ? changes.IsDriveIndexed != changes.InitialIsDriveIndexed ? changes.IsDriveIndexed : (bool?)null : changes.IsIndexed;
		var compressed = changes.IsDrive ? changes.IsDriveCompressed != changes.InitialIsDriveCompressed ? changes.IsDriveCompressed : (bool?)null
			: changes.UpdateCompression ? changes.IsCompressed : null;
		var data = new BackgroundServer.FileAttributeChangesData
		{
			ReadOnly = ToOptionalBoolean(changes.IsDrive ? null : changes.IsReadOnly),
			Hidden = ToOptionalBoolean(changes.IsDrive ? null : changes.IsHidden),
			Archive = ToOptionalBoolean(changes.IsDrive ? null : changes.IsArchive),
			Indexed = ToOptionalBoolean(indexed),
			Compressed = ToOptionalBoolean(compressed),
			Encrypted = ToOptionalBoolean(!changes.IsDrive && changes.UpdateEncryption ? changes.IsEncrypted : null),
			ApplyToContents = changes.ApplyToContents,
		};
		var selectedPaths = paths.ToArray();
		var summary = await RunOperationAsync((server, id) => rename is null ? server.SubmitAttributeChanges(id, selectedPaths, data)
			: server.SubmitPropertyChanges(id, selectedPaths, data, ToData(rename.Item), rename.NewName), cancellationToken).ConfigureAwait(false);

		return ToResult(summary);
	}

	public void Dispose()
	{
		lock (_syncRoot)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_changed.TrySetCanceled();
			Changed = null;
		}

		_shutdown.Cancel();
	}

	private Task<BackgroundServer.AppService> GetConnection()
	{
		lock (_syncRoot)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);

			return _connection ??= Task.Run(() => new BackgroundServer.AppService(Environment.ProcessId));
		}
	}

	private async Task SetActiveWindowAsync(nint windowHandle)
	{
		try
		{
			var server = await GetConnection().ConfigureAwait(false);
			server.SetActiveWindow(windowHandle.ToInt64());
		}
		catch (Exception exception)
		{
			Debug.WriteLine($"Files.BackgroundServer could not activate a window: {exception}");
		}
	}

	private static BackgroundServer.ThumbnailCacheKeyData ToData(ThumbnailCacheKey key)
	{
		return new BackgroundServer.ThumbnailCacheKeyData
		{
			SourceId = key.SourceId.Value,
			ItemId = key.ItemId,
			RequestedSize = key.RequestedSize,
			Mode = (int)key.Mode,
		};
	}

	private static BackgroundServer.ThumbnailCacheEntryData ToData(ThumbnailCacheEntry entry)
	{
		return new BackgroundServer.ThumbnailCacheEntryData
		{
			HasValue = true,
			ContentBase64 = Convert.ToBase64String(entry.Content.Span),
			ContentType = entry.ContentType,
			IsFallback = entry.IsFallback,
			Format = (int)entry.Format,
			PixelWidth = entry.PixelWidth,
			PixelHeight = entry.PixelHeight,
		};
	}

	private static ThumbnailCacheEntry FromData(BackgroundServer.ThumbnailCacheEntryData data)
	{
		return new ThumbnailCacheEntry(Convert.FromBase64String(data.ContentBase64), data.ContentType, data.IsFallback,
			(ThumbnailContentFormat)data.Format, data.PixelWidth, data.PixelHeight);
	}

	private async Task ObserveAsync()
	{
		while (!_shutdown.IsCancellationRequested)
		{
			Task<BackgroundServer.AppService>? connection = null;
			try
			{
				connection = GetConnection();
				var server = await connection.WaitAsync(_shutdown.Token).ConfigureAwait(false);
				while (!_shutdown.IsCancellationRequested)
				{
					var state = await ReadOperationsAsync(server).ConfigureAwait(false);
					PublishState(connection, state);
					await server.WaitForOperationChangesAsync(state.InstanceId, state.Revision).AsTask(_shutdown.Token).ConfigureAwait(false);
				}
			}
			catch (Exception) when (_shutdown.IsCancellationRequested)
			{
				break;
			}
			catch (Exception error)
			{
				UIDiagnosticLog.Write("Operations", $"Server connection failed: {error}");
				lock (_syncRoot)
				{
					_connectionError = error;
					if (ReferenceEquals(connection, _connection))
					{
						_connection = null;
						_ftpSources.Clear();
					}

					SignalChange();
				}
			}
			finally
			{
				if (connection?.IsCompletedSuccessfully is true)
				{
					TryDisconnect(connection.Result);
				}
			}

			try
			{
				await Task.Delay(TimeSpan.FromSeconds(1), _shutdown.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}

	private Task<BackgroundServer.OperationSummary> SubmitAsync(BackgroundServer.StorageOperationData[] items, CancellationToken cancellationToken)
	{
		return RunOperationAsync((server, id) => server.SubmitStorageOperations(id, items), cancellationToken);
	}

	private async Task<BackgroundServer.OperationSummary> RunOperationAsync(Func<BackgroundServer.AppService, Guid, Guid> submit, CancellationToken cancellationToken)
	{
		var job = await SubmitOperationAsync(Guid.NewGuid(), submit, cancellationToken).ConfigureAwait(false);

		return await WaitForSummaryAsync(job, cancellationToken).ConfigureAwait(false);
	}

	private async Task<StorageJobHandle> SubmitOperationAsync(Guid id, Func<BackgroundServer.AppService, Guid, Guid> submit, CancellationToken cancellationToken)
	{
		if (id == Guid.Empty)
		{
			throw new ArgumentException("A job identity is required.", nameof(id));
		}

		cancellationToken.ThrowIfCancellationRequested();

		var server = await GetConnection().WaitAsync(cancellationToken).ConfigureAwait(false);
		var initial = await ReadOperationsAsync(server).WaitAsync(cancellationToken).ConfigureAwait(false);
		cancellationToken.ThrowIfCancellationRequested();

		var job = new StorageJobHandle(id, initial.InstanceId);
		try
		{
			// Dispatch is never retried, including when the acceptance reply is lost.
			var acceptedId = await Task.Run(() => submit(server, id)).ConfigureAwait(false);
			if (acceptedId != id)
			{
				throw new InvalidOperationException("The server returned a different job identity.");
			}

			return job;
		}
		catch (Exception error)
		{
			throw new StorageJobUnknownException(job, false, "Job acceptance could not be confirmed. Do not resubmit automatically.", error);
		}
	}

	private async Task<BackgroundServer.OperationSummary> WaitForSummaryAsync(StorageJobHandle job, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(job);

		try
		{
			var connection = GetConnection();
			var server = await connection.WaitAsync(cancellationToken).ConfigureAwait(false);
			var accepted = await ReadOperationsAsync(server).WaitAsync(cancellationToken).ConfigureAwait(false);
			PublishState(connection, accepted);
			while (true)
			{
				Task changed;
				lock (_syncRoot)
				{
					ObjectDisposedException.ThrowIf(_disposed, this);

					if (_connectionError is { } error)
					{
						throw new IOException("The operation connection was interrupted.", error);
					}

					if (_serverInstanceId != job.ServerInstanceId)
					{
						throw new IOException("The operation server restarted.");
					}

					var item = _operations.FirstOrDefault(item => item.Id == job.Id);
					if (item.Id != job.Id)
					{
						throw new IOException("The operation is no longer retained by the server.");
					}

					if (IsTerminal(item.State))
					{
						return item;
					}

					changed = _changed.Task;
				}

				await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw new StorageJobObservationCanceledException(job, cancellationToken);
		}
		catch (Exception error) when (error is not StorageJobUnknownException)
		{
			throw new StorageJobUnknownException(job, true, "The accepted job outcome is unknown. Keep its identity for subsequent observation.", error);
		}
	}

	private bool QueueControl(Func<BackgroundServer.AppService, bool> command)
	{
		_ = SendControlAsync(command);

		return true;
	}

	private async Task SendControlAsync(Func<BackgroundServer.AppService, bool> command)
	{
		try
		{
			var server = await GetConnection().ConfigureAwait(false);
			await Task.Run(() => command(server)).ConfigureAwait(false);
		}
		catch (Exception error)
		{
			UIDiagnosticLog.Write("Operations", $"Operation control failed: {error}");
		}
	}

	private void SignalChange()
	{
		var previous = _changed;
		_changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		previous.TrySetResult();
	}

	private void PublishState(Task<BackgroundServer.AppService> connection, (Guid InstanceId, ulong Revision, BackgroundServer.OperationSummary[] Items) state)
	{
		lock (_syncRoot)
		{
			if (_disposed || !ReferenceEquals(connection, _connection) || (_serverInstanceId == state.InstanceId && state.Revision < _revision))
			{
				return;
			}

			_serverInstanceId = state.InstanceId;
			_revision = state.Revision;
			_operations = state.Items;
			_connectionError = null;
			SignalChange();
		}

		Changed?.Invoke(this, EventArgs.Empty);
	}

	private static Task<(Guid InstanceId, ulong Revision, BackgroundServer.OperationSummary[] Items)> ReadOperationsAsync(BackgroundServer.AppService server)
	{
		return Task.Run(() =>
		{
			var state = server.GetOperations();

			return (state.ServerInstanceId, state.Revision, state.GetItems());
		});
	}

	private static void TryDisconnect(BackgroundServer.AppService server)
	{
		try
		{
			server.Dispose();
		}
		catch (Exception error)
		{
			UIDiagnosticLog.Write("Operations", $"Client disconnect failed: {error.GetType().Name}");
		}
	}

	private static bool IsWindowsReference(StorableReference item, bool requireFileSystem)
	{
		return item.SourceId.Value == WindowsStorageSource.DefaultSourceType
			&& (!requireFileSystem || item.LastKnownAddress?.Scheme.Equals(WindowsStorageSource.FileAddressScheme, StringComparison.OrdinalIgnoreCase) is true);
	}

	private static bool IsTerminal(BackgroundServer.OperationState state)
	{
		return state is BackgroundServer.OperationState.Succeeded or BackgroundServer.OperationState.Failed or BackgroundServer.OperationState.Canceled;
	}

	private static int ToOptionalBoolean(bool? value)
	{
		return value is null ? -1 : value.Value ? 1 : 0;
	}

	private static StorageOperationResult ToResult(BackgroundServer.OperationSummary summary)
	{
		if (summary.State == BackgroundServer.OperationState.Canceled)
		{
			throw new OperationCanceledException("The server acknowledged operation cancellation.");
		}

		if (summary.State == BackgroundServer.OperationState.Failed)
		{
			return new StorageOperationResult(false, null, new COMException(summary.ErrorMessage, summary.ErrorCode), transfer: StorageOperationStatusMapper.GetTransferOutcome(summary));
		}

		var data = summary.ResultItem;
		var item = summary.HasResultItem ? new StorableReference(new StorageSourceId(data.SourceId), data.ItemId,
			data.HasAddress ? new StorageAddress(data.AddressScheme, data.AddressValue) : null) : null;

		return new StorageOperationResult(true, item, skipped: summary.Skipped, transfer: StorageOperationStatusMapper.GetTransferOutcome(summary));
	}

	private bool IsTransferSource(StorageItemLocator source)
	{
		if (IsWindowsReference(source.Root, false))
		{
			return true;
		}

		lock (_syncRoot)
		{
			return _ftpSources.Contains(source.Root.SourceId.Value);
		}
	}

	private static BackgroundServer.StorageOperationData ToData(StorageOperationRequest request)
	{
		var data = request switch
		{
			DeleteOperationRequest delete => new BackgroundServer.StorageOperationData { Kind = BackgroundServer.StorageOperationKind.Delete, Item = ToData(delete.Item), Permanently = delete.Permanently },
			RenameOperationRequest rename => new BackgroundServer.StorageOperationData { Kind = BackgroundServer.StorageOperationKind.Rename, Item = ToData(rename.Item), Name = rename.NewName },
			CreateItemOperationRequest create => new BackgroundServer.StorageOperationData { Kind = create.Kind == StorageItemKind.File
				? BackgroundServer.StorageOperationKind.CreateFile : BackgroundServer.StorageOperationKind.CreateFolder,
				Item = ToData(create.Parent), Name = create.Name, ConflictBehavior = (int)create.ConflictBehavior },
			_ => throw new NotSupportedException("The operation request has no server contract."),
		};
		data.DisplayName = Path.GetFileName(data.Item.AddressValue.TrimEnd('\\', '/'));

		return data;
	}

	private static BackgroundServer.StorageReferenceData ToData(StorableReference reference)
	{
		return new BackgroundServer.StorageReferenceData { SourceId = reference.SourceId.Value, ItemId = reference.ItemId, HasAddress = reference.LastKnownAddress is not null,
			AddressScheme = reference.LastKnownAddress?.Scheme ?? string.Empty, AddressValue = reference.LastKnownAddress?.Value ?? string.Empty };
	}
}

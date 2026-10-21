// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.InteropServices;
using Files.Core.Storage;
using Files.Core.Windows;
using Files.Infrastructure;
using Files.ItemProperties;

namespace Files.StorageOperations;

internal sealed class WinRTStorageOperationService : IStorageOperationService, IOperationStatusSource, IDisposable
{
	private readonly Lock _syncRoot = new();
	private readonly CancellationTokenSource _shutdown = new();
	private readonly Task _observer;
	private Task<Operations.AppService>? _connection;
	private Operations.OperationSummary[] _operations = [];
	private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private Guid _serverInstanceId;
	private ulong _revision;
	private Exception? _connectionError;
	private bool _disposed;

	public event EventHandler? Changed;

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
			CopyOperationRequest copy => IsWindowsReference(copy.Item, true) && IsWindowsReference(copy.DestinationFolder, true),
			MoveOperationRequest move => IsWindowsReference(move.Item, true) && IsWindowsReference(move.DestinationFolder, true),
			DeleteOperationRequest delete => IsWindowsReference(delete.Item, false),
			_ => false,
		};
	}

	public async ValueTask<StorageOperationResult> ExecuteAsync(StorageOperationRequest request, IProgress<StorageOperationProgress>? progress = null, CancellationToken cancellationToken = default,
		IStorageOperationControl? operationControl = null)
	{
		if (!CanHandle(request))
		{
			return new StorageOperationResult(false, null, new NotSupportedException("The operation server does not support this storage request."));
		}

		// Once accepted, lifetime and controls belong to the server, including single-item commands.
		var result = await SubmitAsync([ToData(request)], cancellationToken).ConfigureAwait(false);

		return ToResult(result);
	}

	internal Task<Operations.AppService> ConnectAsync()
	{
		return GetConnection();
	}

	public IReadOnlyList<StorageOperationStatus> GetOperations()
	{
		lock (_syncRoot)
		{
			return _operations.Select(StorageOperationStatusMapper.Convert).ToArray();
		}
	}

	internal async Task ExecuteBatchAsync(IReadOnlyList<StorageOperationRequest> requests, CancellationToken cancellationToken)
	{
		var items = requests.Select(ToData).ToArray();
		var summary = await SubmitAsync(items, cancellationToken).ConfigureAwait(false);
		var result = ToResult(summary);
		if (!result.Succeeded)
		{
			throw result.Error!;
		}
	}

	internal async Task TransferPathsAsync(IReadOnlyList<string> paths, StorableReference destination, bool move, CancellationToken cancellationToken)
	{
		var items = paths.Select(path => new Operations.StorageOperationData
		{
			Kind = move ? Operations.StorageOperationKind.Move : Operations.StorageOperationKind.Copy,
			Item = new Operations.StorageReferenceData { SourceId = WindowsStorageSource.DefaultSourceType, ItemId = string.Empty, HasAddress = true,
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

	internal async Task InvokeNewItemAsync(string folderPath, uint commandOffset, string name, CancellationToken cancellationToken)
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
		var data = new Operations.FileAttributeChangesData
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

	private Task<Operations.AppService> GetConnection()
	{
		lock (_syncRoot)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);

			return _connection ??= Task.Run(() => new Operations.AppService(Environment.ProcessId));
		}
	}

	private async Task ObserveAsync()
	{
		while (!_shutdown.IsCancellationRequested)
		{
			Task<Operations.AppService>? connection = null;
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

	private Task<Operations.OperationSummary> SubmitAsync(Operations.StorageOperationData[] items, CancellationToken cancellationToken)
	{
		return RunOperationAsync((server, id) => server.SubmitStorageOperations(id, items), cancellationToken);
	}

	private async Task<Operations.OperationSummary> RunOperationAsync(Func<Operations.AppService, Guid, Guid> submit, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var connection = GetConnection();
		var server = await connection.WaitAsync(cancellationToken).ConfigureAwait(false);
		var initial = await ReadOperationsAsync(server).ConfigureAwait(false);
		cancellationToken.ThrowIfCancellationRequested();

		var id = Guid.NewGuid();
		// Never retry submission automatically: a lost reply does not prove that the server rejected it.
		await Task.Run(() => submit(server, id)).ConfigureAwait(false);
		var accepted = await ReadOperationsAsync(server).ConfigureAwait(false);
		PublishState(connection, accepted);
		while (true)
		{
			Task changed;
			lock (_syncRoot)
			{
				ObjectDisposedException.ThrowIf(_disposed, this);

				if (_connectionError is { } error)
				{
					throw new IOException("The operation connection was interrupted. Its outcome is unknown; the operation was not retried.", error);
				}

				if (_serverInstanceId != Guid.Empty && _serverInstanceId != initial.InstanceId)
				{
					throw new IOException("The operation server restarted. The previous operation outcome is unknown.");
				}

				var item = _operations.FirstOrDefault(item => item.Id == id);
				if (IsTerminal(item.State) && item.Id == id)
				{
					return item;
				}

				if (item.Id != id && _revision >= accepted.Revision)
				{
					throw new IOException("The operation is no longer retained by the server. Its outcome cannot be recovered.");
				}

				changed = _changed.Task;
			}

			// UI/navigation cancellation detaches this waiter; Status Center sends explicit job cancellation.
			await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	private bool QueueControl(Func<Operations.AppService, bool> command)
	{
		_ = SendControlAsync(command);

		return true;
	}

	private async Task SendControlAsync(Func<Operations.AppService, bool> command)
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

	private void PublishState(Task<Operations.AppService> connection, (Guid InstanceId, ulong Revision, Operations.OperationSummary[] Items) state)
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

	private static Task<(Guid InstanceId, ulong Revision, Operations.OperationSummary[] Items)> ReadOperationsAsync(Operations.AppService server)
	{
		return Task.Run(() =>
		{
			var state = server.GetOperations();

			return (state.ServerInstanceId, state.Revision, state.GetItems());
		});
	}

	private static void TryDisconnect(Operations.AppService server)
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

	private static bool IsTerminal(Operations.OperationState state)
	{
		return state is Operations.OperationState.Succeeded or Operations.OperationState.Failed or Operations.OperationState.Canceled;
	}

	private static int ToOptionalBoolean(bool? value)
	{
		return value is null ? -1 : value.Value ? 1 : 0;
	}

	private static StorageOperationResult ToResult(Operations.OperationSummary summary)
	{
		if (summary.State == Operations.OperationState.Canceled)
		{
			throw new OperationCanceledException("The server acknowledged operation cancellation.");
		}

		if (summary.State == Operations.OperationState.Failed)
		{
			return new StorageOperationResult(false, null, new COMException(summary.ErrorMessage, summary.ErrorCode));
		}

		var data = summary.ResultItem;
		var item = summary.HasResultItem ? new StorableReference(new StorageSourceId(data.SourceId), data.ItemId,
			data.HasAddress ? new StorageAddress(data.AddressScheme, data.AddressValue) : null) : null;

		return new StorageOperationResult(true, item, skipped: summary.Skipped);
	}

	private static Operations.StorageOperationData ToData(StorageOperationRequest request)
	{
		var data = request switch
		{
			CopyOperationRequest copy => new Operations.StorageOperationData { Kind = Operations.StorageOperationKind.Copy, Item = ToData(copy.Item), Destination = ToData(copy.DestinationFolder),
				Name = copy.NewName ?? string.Empty, ConflictBehavior = (int)copy.ConflictBehavior },
			MoveOperationRequest move => new Operations.StorageOperationData { Kind = Operations.StorageOperationKind.Move, Item = ToData(move.Item), Destination = ToData(move.DestinationFolder),
				Name = move.NewName ?? string.Empty, ConflictBehavior = (int)move.ConflictBehavior },
			DeleteOperationRequest delete => new Operations.StorageOperationData { Kind = Operations.StorageOperationKind.Delete, Item = ToData(delete.Item), Permanently = delete.Permanently },
			RenameOperationRequest rename => new Operations.StorageOperationData { Kind = Operations.StorageOperationKind.Rename, Item = ToData(rename.Item), Name = rename.NewName },
			CreateItemOperationRequest create => new Operations.StorageOperationData { Kind = create.Kind == StorageItemKind.File
				? Operations.StorageOperationKind.CreateFile : Operations.StorageOperationKind.CreateFolder,
				Item = ToData(create.Parent), Name = create.Name, ConflictBehavior = (int)create.ConflictBehavior },
			_ => throw new NotSupportedException("The operation request has no server contract."),
		};
		data.DisplayName = Path.GetFileName(data.Item.AddressValue.TrimEnd('\\', '/'));

		return data;
	}

	private static Operations.StorageReferenceData ToData(StorableReference reference)
	{
		return new Operations.StorageReferenceData { SourceId = reference.SourceId.Value, ItemId = reference.ItemId, HasAddress = reference.LastKnownAddress is not null,
			AddressScheme = reference.LastKnownAddress?.Scheme ?? string.Empty, AddressValue = reference.LastKnownAddress?.Value ?? string.Empty };
	}
}

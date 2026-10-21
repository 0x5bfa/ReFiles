// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using Files.Core.Storage;
using Files.Infrastructure;

namespace Files.StorageOperations;

internal sealed class StorageOperationTracker : IDisposable
{
	private readonly Lock _syncRoot = new();
	private IOperationStatusSource? _server;
	private readonly Dictionary<Guid, StorageOperationProgressDisplay> _progress = [];
	private StorageOperationStatus[] _operations = [];
	private bool _disposed;

	public event EventHandler? Changed;

	internal StorageOperationTracker(IOperationStatusSource? server = null)
	{
		if (server is not null)
		{
			Attach(server);
		}
	}

	internal void Attach(IOperationStatusSource server)
	{
		if (_server is not null)
		{
			_server.Changed -= Server_Changed;
		}

		_server = server;
		_server.Changed += Server_Changed;
		Update();
	}

	public IReadOnlyList<StorageOperationStatus> GetOperations()
	{
		lock (_syncRoot)
		{
			return (StorageOperationStatus[])_operations.Clone();
		}
	}

	public bool RequestCancellation(Guid id) => _server?.Cancel(id) ?? false;

	public bool RequestPause(Guid id) => _server?.SetPaused(id, true) ?? false;

	public bool RequestResume(Guid id) => _server?.SetPaused(id, false) ?? false;

	public bool ResolveInterruption(Guid id, Guid interruptionId, StorageOperationInterruptionResponse response) => _server?.ResolveInterruption(id, interruptionId, response) ?? false;

	public bool Remove(Guid id) => _server?.Forget(id) ?? false;

	public void ClearCompleted()
	{
		foreach (var operation in GetOperations().Where(static operation => !IsActiveState(operation.State)))
		{
			_server?.Forget(operation.Id);
		}
	}

	public void Dispose()
	{
		lock (_syncRoot)
		{
			_disposed = true;
			if (_server is not null)
			{
				_server.Changed -= Server_Changed;
			}
			_progress.Clear();
			_operations = [];
			Changed = null;
		}
	}

	private void Server_Changed(object? sender, EventArgs args)
	{
		Update();
	}

	private void Update()
	{
		var summaries = _server?.GetOperations() ?? Array.Empty<StorageOperationStatus>();
		lock (_syncRoot)
		{
			if (_disposed)
			{
				return;
			}

			var retained = summaries.Select(static summary => summary.Id).ToHashSet();
			foreach (var id in _progress.Keys.Where(id => !retained.Contains(id)).ToArray())
			{
				_progress.Remove(id);
			}

			_operations = summaries.OrderByDescending(static summary => summary.StartedAt).Select(summary =>
			{
				if (!_progress.TryGetValue(summary.Id, out var display))
				{
					display = new StorageOperationProgressDisplay();
					_progress.Add(summary.Id, display);
				}

				return display.Update(summary);
			}).ToArray();
		}

		try
		{
			Changed?.Invoke(this, EventArgs.Empty);
		}
		catch (Exception error)
		{
			UIDiagnosticLog.Write("Operations", $"Status observer failed: {error}");
		}
	}

	private static bool IsActiveState(TrackedStorageOperationState state)
	{
		return state is not (TrackedStorageOperationState.Succeeded or TrackedStorageOperationState.Failed or TrackedStorageOperationState.Canceled);
	}
}

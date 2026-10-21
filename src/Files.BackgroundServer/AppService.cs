// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Windows.Foundation;

namespace Files.BackgroundServer;

/// <summary>Connects a UI process to the shared operation host.</summary>
public sealed class AppService : IDisposable
{
	private readonly CancellationTokenSource _disconnected = new();
	private readonly CancellationToken _sessionToken;
	private Process? _process;
	private IDisposable? _clientLease;
	private int _disposed;

	/// <summary>Attaches a client without transferring ownership of accepted operations.</summary>
	/// <param name="processId">The client process identity.</param>
	public AppService(int processId)
	{
		_sessionToken = _disconnected.Token;
		try
		{
			_clientLease = Program.Lifetime.Acquire(isClient: true);
			_process = Process.GetProcessById(processId);
			_process.Exited += Process_Exited;
			_process.EnableRaisingEvents = true;
		}
		catch
		{
			Dispose();

			throw;
		}
	}

	/// <summary>Captures current operation state for this server instance.</summary>
	/// <returns>A bounded snapshot whose items are returned by value.</returns>
	public OperationList GetOperations()
	{
		using var request = AcquireRequest();

		return Program.Operations.GetOperations();
	}

	/// <summary>Waits for a revision change or a bounded refresh timeout.</summary>
	/// <param name="serverInstanceId">The server identity from the last snapshot.</param>
	/// <param name="knownRevision">The revision from the last snapshot.</param>
	/// <returns>A cancelable observation request; canceling it never cancels an operation.</returns>
	public IAsyncAction WaitForOperationChangesAsync(Guid serverInstanceId, ulong knownRevision)
	{
		return AsyncInfo.Run(async cancellationToken =>
		{
			using var request = AcquireRequest();
			using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sessionToken);
			await Program.Operations.WaitForChangesAsync(serverInstanceId, knownRevision, cancellation.Token).ConfigureAwait(false);
		});
	}

	/// <summary>Requests cooperative cancellation without claiming completion.</summary>
	/// <param name="operationId">The accepted operation identity.</param>
	/// <returns>Whether the operation is still active and accepts cancellation.</returns>
	public bool CancelOperation(Guid operationId)
	{
		using var request = AcquireRequest();

		return Program.Operations.RequestCancellation(operationId);
	}

	/// <summary>Accepts an entire storage batch before starting its item loop.</summary>
	/// <param name="operationId">A new caller-generated operation identity.</param>
	/// <param name="items">The complete batch intent.</param>
	/// <returns>The accepted operation identity.</returns>
	public Guid SubmitStorageOperations(Guid operationId, [ReadOnlyArray] StorageOperationData[] items)
	{
		using var request = AcquireRequest();

		return Program.Operations.Submit(new StorageOperationBatch(operationId, items));
	}

	/// <summary>Accepts attribute changes for execution independently of the UI.</summary>
	public Guid SubmitAttributeChanges(Guid operationId, [ReadOnlyArray] string[] paths, FileAttributeChangesData changes)
	{
		using var request = AcquireRequest();

		return Program.Operations.Submit(new FileAttributeOperation(operationId, paths, changes));
	}

	/// <summary>Accepts attribute changes and a following rename as one server-owned operation.</summary>
	public Guid SubmitPropertyChanges(Guid operationId, [ReadOnlyArray] string[] paths, FileAttributeChangesData changes, StorageReferenceData item, string newName)
	{
		using var request = AcquireRequest();
		var rename = new StorageOperationData { Kind = StorageOperationKind.Rename, Item = item, Name = newName, DisplayName = newName };

		return Program.Operations.Submit(new FileAttributeOperation(operationId, paths, changes, rename));
	}

	/// <summary>Invokes a Shell New menu item in the server's own Shell apartment.</summary>
	public Guid SubmitShellNewItem(Guid operationId, string folderPath, uint commandOffset, string name)
	{
		using var request = AcquireRequest();
		ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		return Program.Operations.Submit(new ShellNewItemOperation(operationId, folderPath, commandOffset, name));
	}

	/// <summary>Requests a pause or resume when the operation supports it.</summary>
	public bool SetOperationPaused(Guid operationId, bool paused)
	{
		using var request = AcquireRequest();

		return Program.Operations.SetPaused(operationId, paused);
	}

	/// <summary>Answers a specific interruption, rejecting obsolete decisions.</summary>
	public bool ResolveOperationInterruption(Guid operationId, Guid interruptionId, int decision, bool applyToAll)
	{
		using var request = AcquireRequest();

		return Program.Operations.ResolveInterruption(operationId, interruptionId, decision, applyToAll);
	}

	/// <summary>Removes a terminal operation from this server's retained history.</summary>
	/// <param name="operationId">The operation identity.</param>
	/// <returns>Whether a terminal operation was removed.</returns>
	public bool ForgetOperation(Guid operationId)
	{
		using var request = AcquireRequest();

		return Program.Operations.Forget(operationId);
	}

	/// <summary>Disconnects this client and its observation requests, leaving operations running.</summary>
	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		if (Interlocked.Exchange(ref _process, null) is { } process)
		{
			process.Exited -= Process_Exited;
			process.Dispose();
		}

		_disconnected.Cancel();
		_disconnected.Dispose();
		Interlocked.Exchange(ref _clientLease, null)?.Dispose();
		Program.Operations.ScheduleUnattendedCancellation();
		GC.SuppressFinalize(this);
	}

	~AppService() => Dispose();

	private IDisposable AcquireRequest()
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

		return Program.Lifetime.Acquire();
	}

	private void Process_Exited(object? sender, EventArgs e)
	{
		Dispose();
	}
}

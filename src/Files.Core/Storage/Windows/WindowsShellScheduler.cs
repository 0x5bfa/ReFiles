// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Files.Core.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Owns the message-pumped STA lanes used by the Windows storage source.
/// </summary>
/// <remarks>
/// Delegates must be synchronous. Cancellation applies while work is waiting to
/// start; a running delegate must finish or observe cancellation itself. Raw COM
/// interfaces must never escape a delegate unless a private wrapper routes every
/// later access back through the same ordered lane.
/// </remarks>
[SupportedOSPlatform("windows5.1.2600")]
public sealed class WindowsShellScheduler : IWindowsShellScheduler
{
	[ThreadStatic]
	private static MessagePumpedStaScheduler? _activeScheduler;

	private readonly Lock _syncRoot = new();
	private readonly MessagePumpedStaScheduler _orderedScheduler;
	private readonly MessagePumpedStaScheduler _concurrentScheduler;
	private readonly MessagePumpedStaScheduler _operationScheduler;
	private Task? _disposeTask;

	/// <summary>Initializes Windows Shell scheduler lanes.</summary>
	/// <param name="concurrentWorkerCount">The number of concurrent workers.</param>
	public WindowsShellScheduler(int? concurrentWorkerCount = null)
	{
		var workerCount = concurrentWorkerCount
			?? Math.Min(Math.Max(Environment.ProcessorCount, 2), 4);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workerCount);

		_orderedScheduler = new MessagePumpedStaScheduler("Files Windows Shell STA", workerCount: 1);
		_concurrentScheduler = new MessagePumpedStaScheduler("Files Windows Shell concurrent STA", workerCount);
		_operationScheduler = new MessagePumpedStaScheduler("Files Windows Shell operation STA", workerCount: 1);
		CoreDiagnosticLog.Write("WindowsShellScheduler", $"created concurrentWorkers={workerCount}");
	}

	/// <summary>Invokes a delegate on the ordered Shell lane.</summary>
	/// <typeparam name="T">The delegate result type.</typeparam>
	/// <param name="action">The synchronous delegate.</param>
	/// <param name="cancellationToken">The token used to cancel queuing.</param>
	/// <returns>A task containing the delegate result.</returns>
	public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
	{
		return _orderedScheduler.InvokeAsync(action, cancellationToken);
	}

	/// <summary>Invokes a delegate on a concurrent Shell lane.</summary>
	/// <typeparam name="T">The delegate result type.</typeparam>
	/// <param name="action">The synchronous delegate.</param>
	/// <param name="cancellationToken">The token used to cancel queuing.</param>
	/// <returns>A task containing the delegate result.</returns>
	public Task<T> InvokeConcurrentAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
	{
		return _concurrentScheduler.InvokeAsync(action, cancellationToken);
	}

	/// <summary>Invokes a delegate on the ordered operation lane.</summary>
	/// <typeparam name="T">The delegate result type.</typeparam>
	/// <param name="action">The synchronous delegate.</param>
	/// <param name="cancellationToken">The token used to cancel queuing.</param>
	/// <returns>A task containing the delegate result.</returns>
	public Task<T> InvokeOperationAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
	{
		return _operationScheduler.InvokeAsync(action, cancellationToken);
	}

	/// <summary>Stops all scheduler lanes.</summary>
	/// <returns>A value task that represents scheduler disposal.</returns>
	public ValueTask DisposeAsync()
	{
		lock (_syncRoot)
		{
			_disposeTask ??= DisposeCoreAsync();

			return new ValueTask(_disposeTask);
		}
	}

	private async Task DisposeCoreAsync()
	{
		await Task.WhenAll(_orderedScheduler.DisposeAsync().AsTask(), _concurrentScheduler.DisposeAsync().AsTask(), _operationScheduler.DisposeAsync().AsTask()).ConfigureAwait(false);

		GC.SuppressFinalize(this);
	}

	private abstract class WorkItem
	{
		public abstract void Execute();

		public abstract void SetException(Exception exception);
	}

	private sealed class MessagePumpedStaScheduler : IAsyncDisposable
	{
		private readonly string _threadName;
		private readonly Lock _stateLock = new();
		private readonly ConcurrentQueue<WorkItem> _workItems = [];
		private readonly Semaphore _workAvailable = new(0, int.MaxValue);
		private readonly TaskCompletionSource<bool> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly int _workerCount;
		private long _nextWorkItemId;
		private int _remainingWorkers;
		private bool _isStopping;
		private Exception? _terminalException;
		private Task? _disposeTask;

		public MessagePumpedStaScheduler(string threadName, int workerCount)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(threadName);
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workerCount);

			_threadName = threadName;
			_workerCount = workerCount;
			_remainingWorkers = workerCount;
			CoreDiagnosticLog.Write("WindowsShellScheduler", $"lane created name={_threadName} workers={_workerCount}");

			for (var index = 0; index < workerCount; index++)
			{
				var thread = new Thread(Run)
				{
					IsBackground = true,
					Name = workerCount == 1 ? threadName : $"{threadName} {index + 1}",
				};
				thread.SetApartmentState(ApartmentState.STA);
				thread.Start();
			}
		}

		public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken)
		{
			ArgumentNullException.ThrowIfNull(action);
			cancellationToken.ThrowIfCancellationRequested();

			if (ReferenceEquals(_activeScheduler, this))
			{
				CoreDiagnosticLog.Write("WindowsShellScheduler", $"invoke inline lane={_threadName} action={action.Method.Name}");
				return InvokeInline(action, cancellationToken);
			}

			lock (_stateLock)
			{
				if (_terminalException is not null)
				{
					return Task.FromException<T>(_terminalException);
				}
			}

			return Enqueue(action, cancellationToken);
		}

		public ValueTask DisposeAsync()
		{
			List<WorkItem>? pendingWork = null;
			Exception? stopException = null;

			lock (_stateLock)
			{
				if (_disposeTask is null)
				{
					CoreDiagnosticLog.Write("WindowsShellScheduler", $"dispose lane={_threadName}");
					stopException = new ObjectDisposedException(nameof(WindowsShellScheduler));
					pendingWork = StopLocked(stopException);
					_disposeTask = CompleteDisposalAsync();
				}
			}

			if (pendingWork is not null && stopException is not null)
			{
				SetPendingExceptions(pendingWork, stopException);
			}

			return new ValueTask(_disposeTask!);
		}

		private Task<T> InvokeInline<T>(Func<T> action, CancellationToken cancellationToken)
		{
			var startTimestamp = Stopwatch.GetTimestamp();
			try
			{
				cancellationToken.ThrowIfCancellationRequested();

				var result = action();
				CoreDiagnosticLog.Write("WindowsShellScheduler", $"invoke inline END lane={_threadName} action={action.Method.Name} elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");

				return Task.FromResult(result);
			}
			catch (OperationCanceledException exception)
				when (exception.CancellationToken.IsCancellationRequested)
			{
				CoreDiagnosticLog.Write(
					"WindowsShellScheduler",
					$"invoke inline CANCELLED lane={_threadName} action={action.Method.Name} elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");
				return Task.FromCanceled<T>(exception.CancellationToken);
			}
			catch (Exception exception)
			{
				CoreDiagnosticLog.Write(
					"WindowsShellScheduler",
					$"invoke inline ERROR lane={_threadName} action={action.Method.Name} type={exception.GetType().Name} elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");
				return Task.FromException<T>(exception);
			}
		}

		private Task<T> Enqueue<T>(Func<T> action, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var workItem = new WorkItem<T>(_threadName, Interlocked.Increment(ref _nextWorkItemId), action, cancellationToken);
			var queueLength = 0;

			lock (_stateLock)
			{
				if (_terminalException is not null)
				{
					return Task.FromException<T>(_terminalException);
				}

				if (!workItem.TryPrepareForQueue())
				{
					workItem.Dispose();

					return workItem.Task;
				}

				_workItems.Enqueue(workItem);
				workItem.RegisterCancellation();
				_workAvailable.Release();
				queueLength = _workItems.Count;
			}

			CoreDiagnosticLog.Write("WindowsShellScheduler", $"queued lane={_threadName} id={workItem.Id} action={workItem.ActionName} queue={queueLength}");

			return workItem.Task;
		}

		[SupportedOSPlatform("windows5.1.2600")]
		private unsafe void Run()
		{
			var oleInitialized = false;
			CoreDiagnosticLog.Write("WindowsShellScheduler", $"STA thread START lane={_threadName} thread={Environment.CurrentManagedThreadId}");

			try
			{
				var result = PInvoke.OleInitialize(null);

				if (result.Failed)
				{
					Fault(CreateOleInitializationException(result));

					return;
				}

				oleInitialized = true;
				_activeScheduler = this;
				CoreDiagnosticLog.Write("WindowsShellScheduler", $"STA thread READY lane={_threadName} thread={Environment.CurrentManagedThreadId}");

				PInvoke.PeekMessage(out _, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);

				while (!IsStopping())
				{
					PumpMessages();

					if (TryExecuteNextWorkItem())
					{
						continue;
					}

					WaitForWorkOrMessage();
				}
			}
			catch (Exception exception)
			{
				Fault(exception);
			}
			finally
			{
				_activeScheduler = null;

				if (oleInitialized)
				{
					PInvoke.OleUninitialize();
				}

				if (Interlocked.Decrement(ref _remainingWorkers) == 0)
				{
					_stopped.TrySetResult(true);
				}

				CoreDiagnosticLog.Write("WindowsShellScheduler", $"STA thread END lane={_threadName} thread={Environment.CurrentManagedThreadId}");
			}
		}

		private bool IsStopping()
		{
			lock (_stateLock)
			{
				return _isStopping;
			}
		}

		private bool TryExecuteNextWorkItem()
		{
			if (!_workAvailable.WaitOne(0))
			{
				return false;
			}

			ExecuteDequeuedWorkItem();

			return true;
		}

		private void ExecuteDequeuedWorkItem()
		{
			if (_workItems.TryDequeue(out var workItem))
			{
				workItem.Execute();
			}
		}

		[SupportedOSPlatform("windows5.1.2600")]
		private unsafe void WaitForWorkOrMessage()
		{
			var safeWaitHandle = _workAvailable.SafeWaitHandle;
			var addedReference = false;

			try
			{
				safeWaitHandle.DangerousAddRef(ref addedReference);

				Span<HANDLE> handles = stackalloc HANDLE[1];
				handles[0] = (HANDLE)safeWaitHandle.DangerousGetHandle();

				var result = PInvoke.MsgWaitForMultipleObjectsEx(handles, uint.MaxValue, QUEUE_STATUS_FLAGS.QS_ALLINPUT, MSG_WAIT_FOR_MULTIPLE_OBJECTS_EX_FLAGS.MWMO_INPUTAVAILABLE);
				var resultValue = (uint)result;

				if (resultValue == (uint)WAIT_EVENT.WAIT_OBJECT_0)
				{
					ExecuteDequeuedWorkItem();

					return;
				}

				if (resultValue == (uint)WAIT_EVENT.WAIT_OBJECT_0 + handles.Length)
				{
					PumpMessages();

					return;
				}

				if (result == WAIT_EVENT.WAIT_IO_COMPLETION)
				{
					return;
				}

				if (result == WAIT_EVENT.WAIT_FAILED)
				{
					throw new Win32Exception(Marshal.GetLastPInvokeError());
				}

				throw new InvalidOperationException($"Unexpected STA wait result: {result}.");
			}
			finally
			{
				if (addedReference)
				{
					safeWaitHandle.DangerousRelease();
				}

				GC.KeepAlive(_workAvailable);
			}
		}

		[SupportedOSPlatform("windows5.1.2600")]
		private static void PumpMessages()
		{
			while (PInvoke.PeekMessage(out var message, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
			{
				PInvoke.TranslateMessage(message);
				PInvoke.DispatchMessage(message);
			}
		}

		private void Fault(Exception exception)
		{
			CoreDiagnosticLog.Write("WindowsShellScheduler", $"FAULT lane={_threadName} type={exception.GetType().Name} message={exception.Message}");
			List<WorkItem> pendingWork;

			lock (_stateLock)
			{
				if (_isStopping)
				{
					return;
				}

				pendingWork = StopLocked(exception);
			}

			SetPendingExceptions(pendingWork, exception);
		}

		private List<WorkItem> StopLocked(Exception exception)
		{
			_isStopping = true;
			_terminalException = exception;

			var pendingWork = new List<WorkItem>();

			while (_workItems.TryDequeue(out var workItem))
			{
				pendingWork.Add(workItem);
			}

			for (var index = 0; index < _workerCount; index++)
			{
				try
				{
					_workAvailable.Release();
				}
				catch (SemaphoreFullException)
				{
				}
			}

			CoreDiagnosticLog.Write("WindowsShellScheduler", $"STOP lane={_threadName} pending={pendingWork.Count}");

			return pendingWork;
		}

		private async Task CompleteDisposalAsync()
		{
			await _stopped.Task.ConfigureAwait(false);
			_workAvailable.Dispose();
		}

		private static void SetPendingExceptions(IEnumerable<WorkItem> pendingWork, Exception exception)
		{
			foreach (var workItem in pendingWork)
			{
				workItem.SetException(exception);
			}
		}

		private static InvalidOperationException CreateOleInitializationException(HRESULT result)
		{
			return new InvalidOperationException($"Failed to initialize OLE on a Windows Shell STA. HRESULT: {result}");
		}
	}

	private sealed class WorkItem<T> : WorkItem, IDisposable
	{
		private const int Pending = 0;

		private const int Started = 1;

		private const int Completed = 2;

		private readonly Func<T> _action;
		private readonly string _actionName;
		private readonly long _id;
		private readonly string _laneName;
		private readonly long _queuedTimestamp;

		private readonly CancellationToken _cancellationToken;

		private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

		private CancellationTokenRegistration _cancellationRegistration;

		private int _state;

		public long Id => _id;

		public string ActionName => _actionName;

		public Task<T> Task => _completion.Task;

		public WorkItem(string laneName, long id, Func<T> action, CancellationToken cancellationToken)
		{
			_laneName = laneName;
			_id = id;
			_action = action;
			_actionName = $"{action.Method.DeclaringType?.Name}.{action.Method.Name}";
			_cancellationToken = cancellationToken;
			_queuedTimestamp = Stopwatch.GetTimestamp();
		}

		public bool TryPrepareForQueue()
		{
			if (!_cancellationToken.IsCancellationRequested)
			{
				return true;
			}

			CancelIfPending();

			return false;
		}

		public void RegisterCancellation()
		{
			if (!_cancellationToken.CanBeCanceled)
			{
				return;
			}

			_cancellationRegistration = _cancellationToken.UnsafeRegister(static state => ((WorkItem<T>)state!).CancelIfPending(), this);

			if (_cancellationToken.IsCancellationRequested)
			{
				CancelIfPending();
			}
		}

		public override void Execute()
		{
			if (_cancellationToken.IsCancellationRequested)
			{
				CoreDiagnosticLog.Write("WindowsShellScheduler", $"cancelled before start lane={_laneName} id={_id} action={_actionName} waitMs={Stopwatch.GetElapsedTime(_queuedTimestamp).TotalMilliseconds:F1}");
				CancelIfPending();
				DisposeCancellationRegistration();

				return;
			}

			if (Interlocked.CompareExchange(ref _state, Started, Pending) != Pending)
			{
				DisposeCancellationRegistration();

				return;
			}

			var startTimestamp = Stopwatch.GetTimestamp();
			var outcome = "completed";
			CoreDiagnosticLog.Write(
				"WindowsShellScheduler",
				$"work START lane={_laneName} id={_id} action={_actionName} waitMs={Stopwatch.GetElapsedTime(_queuedTimestamp).TotalMilliseconds:F1} thread={Environment.CurrentManagedThreadId}");

			try
			{
				_completion.TrySetResult(_action());
			}
			catch (OperationCanceledException exception)
				when (exception.CancellationToken.IsCancellationRequested)
			{
				outcome = "cancelled";
				_completion.TrySetCanceled(exception.CancellationToken);
			}
			catch (Exception exception)
			{
				outcome = $"error={exception.GetType().Name}";
				_completion.TrySetException(exception);
			}
			finally
			{
				Volatile.Write(ref _state, Completed);
				DisposeCancellationRegistration();
				CoreDiagnosticLog.Write(
					"WindowsShellScheduler",
					$"work END lane={_laneName} id={_id} action={_actionName} outcome={outcome} elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");
			}
		}

		public override void SetException(Exception exception)
		{
			if (Interlocked.CompareExchange(ref _state, Completed, Pending) == Pending)
			{
				CoreDiagnosticLog.Write("WindowsShellScheduler", $"work STOPPED lane={_laneName} id={_id} action={_actionName} type={exception.GetType().Name}");
				_completion.TrySetException(exception);
			}

			DisposeCancellationRegistration();
		}

		public void Dispose()
		{
			DisposeCancellationRegistration();
		}

		private void CancelIfPending()
		{
			if (Interlocked.CompareExchange(ref _state, Completed, Pending) == Pending)
			{
				CoreDiagnosticLog.Write("WindowsShellScheduler", $"work CANCELLED lane={_laneName} id={_id} action={_actionName} waitMs={Stopwatch.GetElapsedTime(_queuedTimestamp).TotalMilliseconds:F1}");
				_completion.TrySetCanceled(_cancellationToken);
			}
		}

		private void DisposeCancellationRegistration()
		{
			_cancellationRegistration.Dispose();
		}
	}
}

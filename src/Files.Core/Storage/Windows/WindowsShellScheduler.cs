// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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
	private static MessagePumpedStaScheduler? activeScheduler;

	private readonly object syncRoot = new();
	private readonly MessagePumpedStaScheduler orderedScheduler;
	private readonly MessagePumpedStaScheduler concurrentScheduler;
	private readonly MessagePumpedStaScheduler operationScheduler;
	private Task? disposeTask;

	public WindowsShellScheduler(int? concurrentWorkerCount = null)
	{
		var workerCount = concurrentWorkerCount
			?? Math.Min(Math.Max(Environment.ProcessorCount, 2), 4);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workerCount);

		orderedScheduler = new MessagePumpedStaScheduler(
			"Files Windows Shell STA",
			workerCount: 1);
		concurrentScheduler = new MessagePumpedStaScheduler(
			"Files Windows Shell concurrent STA",
			workerCount);
		operationScheduler = new MessagePumpedStaScheduler(
			"Files Windows Shell operation STA",
			workerCount: 1);
	}

	public Task<T> InvokeAsync<T>(
		Func<T> action,
		CancellationToken cancellationToken = default)
	{
		return orderedScheduler.InvokeAsync(action, cancellationToken);
	}

	public Task<T> InvokeConcurrentAsync<T>(
		Func<T> action,
		CancellationToken cancellationToken = default)
	{
		return concurrentScheduler.InvokeAsync(action, cancellationToken);
	}

	public Task<T> InvokeOperationAsync<T>(
		Func<T> action,
		CancellationToken cancellationToken = default)
	{
		return operationScheduler.InvokeAsync(action, cancellationToken);
	}

	public ValueTask DisposeAsync()
	{
		lock (syncRoot)
		{
			disposeTask ??= DisposeCoreAsync();
			return new ValueTask(disposeTask);
		}
	}

	private async Task DisposeCoreAsync()
	{
		await Task.WhenAll(
			orderedScheduler.DisposeAsync().AsTask(),
			concurrentScheduler.DisposeAsync().AsTask(),
			operationScheduler.DisposeAsync().AsTask()).ConfigureAwait(false);

		GC.SuppressFinalize(this);
	}

	private abstract class WorkItem
	{
		public abstract void Execute();

		public abstract void SetException(Exception exception);
	}

	private sealed class MessagePumpedStaScheduler : IAsyncDisposable
	{
		private readonly object stateLock = new();
		private readonly ConcurrentQueue<WorkItem> workItems = [];
		private readonly Semaphore workAvailable = new(0, int.MaxValue);
		private readonly TaskCompletionSource<bool> stopped = new(
			TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly int workerCount;
		private int remainingWorkers;
		private bool isStopping;
		private Exception? terminalException;
		private Task? disposeTask;

		public MessagePumpedStaScheduler(string threadName, int workerCount)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(threadName);
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workerCount);

			this.workerCount = workerCount;
			remainingWorkers = workerCount;

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

		public Task<T> InvokeAsync<T>(
			Func<T> action,
			CancellationToken cancellationToken)
		{
			ArgumentNullException.ThrowIfNull(action);
			cancellationToken.ThrowIfCancellationRequested();

			if (ReferenceEquals(activeScheduler, this))
			{
				return InvokeInline(action, cancellationToken);
			}

			lock (stateLock)
			{
				if (terminalException is not null)
				{
					return Task.FromException<T>(terminalException);
				}
			}

			return Enqueue(action, cancellationToken);
		}

		public ValueTask DisposeAsync()
		{
			List<WorkItem>? pendingWork = null;
			Exception? stopException = null;

			lock (stateLock)
			{
				if (disposeTask is null)
				{
					stopException = new ObjectDisposedException(nameof(WindowsShellScheduler));
					pendingWork = StopLocked(stopException);
					disposeTask = CompleteDisposalAsync();
				}
			}

			if (pendingWork is not null && stopException is not null)
			{
				SetPendingExceptions(pendingWork, stopException);
			}

			return new ValueTask(disposeTask!);
		}

		private static Task<T> InvokeInline<T>(
			Func<T> action,
			CancellationToken cancellationToken)
		{
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				return Task.FromResult(action());
			}
			catch (OperationCanceledException exception)
				when (exception.CancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled<T>(exception.CancellationToken);
			}
			catch (Exception exception)
			{
				return Task.FromException<T>(exception);
			}
		}

		private Task<T> Enqueue<T>(
			Func<T> action,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var workItem = new WorkItem<T>(action, cancellationToken);

			lock (stateLock)
			{
				if (terminalException is not null)
				{
					return Task.FromException<T>(terminalException);
				}

				if (!workItem.TryPrepareForQueue())
				{
					workItem.Dispose();
					return workItem.Task;
				}

				workItems.Enqueue(workItem);
				workItem.RegisterCancellation();
				workAvailable.Release();
			}

			return workItem.Task;
		}

		[SupportedOSPlatform("windows5.1.2600")]
		private unsafe void Run()
		{
			var oleInitialized = false;

			try
			{
				var result = PInvoke.OleInitialize(null);

				if (result.Failed)
				{
					Fault(CreateOleInitializationException(result));
					return;
				}

				oleInitialized = true;
				activeScheduler = this;

				PInvoke.PeekMessage(
					out _,
					default,
					0,
					0,
					PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);

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
				activeScheduler = null;

				if (oleInitialized)
				{
					PInvoke.OleUninitialize();
				}

				if (Interlocked.Decrement(ref remainingWorkers) == 0)
				{
					stopped.TrySetResult(true);
				}
			}
		}

		private bool IsStopping()
		{
			lock (stateLock)
			{
				return isStopping;
			}
		}

		private bool TryExecuteNextWorkItem()
		{
			if (!workAvailable.WaitOne(0))
			{
				return false;
			}

			ExecuteDequeuedWorkItem();
			return true;
		}

		private void ExecuteDequeuedWorkItem()
		{
			if (workItems.TryDequeue(out var workItem))
			{
				workItem.Execute();
			}
		}

		[SupportedOSPlatform("windows5.1.2600")]
		private unsafe void WaitForWorkOrMessage()
		{
			var safeWaitHandle = workAvailable.SafeWaitHandle;
			var addedReference = false;

			try
			{
				safeWaitHandle.DangerousAddRef(ref addedReference);

				Span<HANDLE> handles = stackalloc HANDLE[1];
				handles[0] = (HANDLE)safeWaitHandle.DangerousGetHandle();

				var result = PInvoke.MsgWaitForMultipleObjectsEx(
					handles,
					uint.MaxValue,
					QUEUE_STATUS_FLAGS.QS_ALLINPUT,
					MSG_WAIT_FOR_MULTIPLE_OBJECTS_EX_FLAGS.MWMO_INPUTAVAILABLE);
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

				GC.KeepAlive(workAvailable);
			}
		}

		[SupportedOSPlatform("windows5.1.2600")]
		private static void PumpMessages()
		{
			while (PInvoke.PeekMessage(
				out var message,
				default,
				0,
				0,
				PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
			{
				PInvoke.TranslateMessage(message);
				PInvoke.DispatchMessage(message);
			}
		}

		private void Fault(Exception exception)
		{
			List<WorkItem> pendingWork;

			lock (stateLock)
			{
				if (isStopping)
				{
					return;
				}

				pendingWork = StopLocked(exception);
			}

			SetPendingExceptions(pendingWork, exception);
		}

		private List<WorkItem> StopLocked(Exception exception)
		{
			isStopping = true;
			terminalException = exception;

			var pendingWork = new List<WorkItem>();

			while (workItems.TryDequeue(out var workItem))
			{
				pendingWork.Add(workItem);
			}

			for (var index = 0; index < workerCount; index++)
			{
				try
				{
					workAvailable.Release();
				}
				catch (SemaphoreFullException)
				{
				}
			}

			return pendingWork;
		}

		private async Task CompleteDisposalAsync()
		{
			await stopped.Task.ConfigureAwait(false);
			workAvailable.Dispose();
		}

		private static void SetPendingExceptions(
			IEnumerable<WorkItem> pendingWork,
			Exception exception)
		{
			foreach (var workItem in pendingWork)
			{
				workItem.SetException(exception);
			}
		}

		private static InvalidOperationException CreateOleInitializationException(HRESULT result)
		{
			return new InvalidOperationException(
				$"Failed to initialize OLE on a Windows Shell STA. HRESULT: {result}");
		}
	}

	private sealed class WorkItem<T> : WorkItem, IDisposable
	{
		private const int Pending = 0;
		private const int Started = 1;
		private const int Completed = 2;

		private readonly Func<T> action;
		private readonly CancellationToken cancellationToken;
		private readonly TaskCompletionSource<T> completion = new(
			TaskCreationOptions.RunContinuationsAsynchronously);
		private CancellationTokenRegistration cancellationRegistration;
		private int state;

		public WorkItem(Func<T> action, CancellationToken cancellationToken)
		{
			this.action = action;
			this.cancellationToken = cancellationToken;
		}

		public Task<T> Task => completion.Task;

		public bool TryPrepareForQueue()
		{
			if (!cancellationToken.IsCancellationRequested)
			{
				return true;
			}

			CancelIfPending();
			return false;
		}

		public void RegisterCancellation()
		{
			if (!cancellationToken.CanBeCanceled)
			{
				return;
			}

			cancellationRegistration = cancellationToken.UnsafeRegister(
				static state => ((WorkItem<T>)state!).CancelIfPending(),
				this);

			if (cancellationToken.IsCancellationRequested)
			{
				CancelIfPending();
			}
		}

		public override void Execute()
		{
			if (cancellationToken.IsCancellationRequested)
			{
				CancelIfPending();
				DisposeCancellationRegistration();
				return;
			}

			if (Interlocked.CompareExchange(ref state, Started, Pending) != Pending)
			{
				DisposeCancellationRegistration();
				return;
			}

			try
			{
				completion.TrySetResult(action());
			}
			catch (OperationCanceledException exception)
				when (exception.CancellationToken.IsCancellationRequested)
			{
				completion.TrySetCanceled(exception.CancellationToken);
			}
			catch (Exception exception)
			{
				completion.TrySetException(exception);
			}
			finally
			{
				Volatile.Write(ref state, Completed);
				DisposeCancellationRegistration();
			}
		}

		public override void SetException(Exception exception)
		{
			if (Interlocked.CompareExchange(ref state, Completed, Pending) == Pending)
			{
				completion.TrySetException(exception);
			}

			DisposeCancellationRegistration();
		}

		public void Dispose()
		{
			DisposeCancellationRegistration();
		}

		private void CancelIfPending()
		{
			if (Interlocked.CompareExchange(ref state, Completed, Pending) == Pending)
			{
				completion.TrySetCanceled(cancellationToken);
			}
		}

		private void DisposeCancellationRegistration()
		{
			cancellationRegistration.Dispose();
		}
	}
}

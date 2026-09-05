// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Diagnostics;
using System.Runtime.InteropServices.Marshalling;
using Files.Core.Storage;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace Files.Core.Windows;

[GeneratedComClass]
internal sealed partial class WindowsFileOperationProgressSink : IFileOperationProgressSink, IOperationsProgressDialog
{
	private const int CanceledHResultValue = unchecked((int)0x800704C7);
	private const int PointerHResultValue = unchecked((int)0x80004003);

	private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(100);

	private readonly IProgress<StorageOperationProgress>? _progress;
	private readonly StorableReference _currentItem;
	private readonly long? _totalBytes;
	private readonly CancellationToken _cancellationToken;
	private readonly IStorageOperationControl? _operationControl;
	private long _lastReportTimestamp;
	private long _lastCompletedBytes = -1;
	private long _pausedTimerDuration;
	private long _timerPausedTimestamp;
	private long _timerStartedTimestamp;
	private int _itemResult;
	private bool _receivedByteProgress;

	internal HRESULT ItemResult => new(Volatile.Read(ref _itemResult));

	internal WindowsFileOperationProgressSink(IProgress<StorageOperationProgress>? progress, StorableReference currentItem, long? totalBytes, CancellationToken cancellationToken,
		IStorageOperationControl? operationControl)
	{
		ArgumentNullException.ThrowIfNull(currentItem);

		if (totalBytes is < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(totalBytes));
		}

		_progress = progress;
		_currentItem = currentItem;
		_totalBytes = totalBytes;
		_cancellationToken = cancellationToken;
		_operationControl = operationControl;
		_lastReportTimestamp = Stopwatch.GetTimestamp();
		_timerStartedTimestamp = _lastReportTimestamp;
	}

	/// <inheritdoc />
	public HRESULT StartProgressDialog(HWND hwndOwner, uint flags)
	{
		ReportEstimatedBytes(0, 0, force: true);

		return GetCancellationResult();
	}

	/// <inheritdoc />
	public HRESULT StopProgressDialog()
	{
		return HRESULT.S_OK;
	}

	/// <inheritdoc />
	public HRESULT SetOperation(SPACTION action)
	{
		return GetCancellationResult();
	}

	/// <inheritdoc />
	public HRESULT SetMode(uint mode)
	{
		return GetCancellationResult();
	}

	/// <inheritdoc />
	public HRESULT UpdateProgress(ulong ullPointsCurrent, ulong ullPointsTotal, ulong ullSizeCurrent, ulong ullSizeTotal, ulong ullItemsCurrent, ulong ullItemsTotal)
	{
		_receivedByteProgress = true;
		ReportBytes(ullSizeCurrent, ullSizeTotal, ullSizeTotal is not 0 && ullSizeCurrent >= ullSizeTotal);

		return GetCancellationResult();
	}

	/// <inheritdoc />
	public HRESULT UpdateLocations(IShellItem psiSource, IShellItem psiTarget, IShellItem psiItem)
	{
		return GetCancellationResult();
	}

	/// <inheritdoc />
	public HRESULT StartOperations()
	{
		var result = GetCancellationResult();
		if (result.Failed)
		{
			return result;
		}

		ReportEstimatedBytes(0, 0, force: true);

		return GetCancellationResult();
	}

	/// <inheritdoc />
	public HRESULT FinishOperations(HRESULT hrResult)
	{
		return HRESULT.S_OK;
	}

	/// <inheritdoc />
	public HRESULT PreRenameItem(uint dwFlags, IShellItem psiItem, PCWSTR pszNewName) => GetCancellationResult();

	/// <inheritdoc />
	public HRESULT PostRenameItem(uint dwFlags, IShellItem psiItem, PCWSTR pszNewName, HRESULT hrRename, IShellItem psiNewlyCreated)
	{
		RecordItemResult(hrRename);

		return GetCancellationResult();
	}

	/// <inheritdoc />
	public HRESULT PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, PCWSTR pszNewName) => GetCancellationResult();

	/// <inheritdoc />
	public HRESULT PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, PCWSTR pszNewName, HRESULT hrMove, IShellItem psiNewlyCreated)
	{
		RecordItemResult(hrMove);

		return GetCancellationResult();
	}

	/// <inheritdoc />
	public HRESULT PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, PCWSTR pszNewName) => GetCancellationResult();

	/// <inheritdoc />
	public HRESULT PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, PCWSTR pszNewName, HRESULT hrCopy, IShellItem psiNewlyCreated)
	{
		RecordItemResult(hrCopy);

		return GetCancellationResult();
	}

	/// <inheritdoc />
	public HRESULT PreDeleteItem(uint dwFlags, IShellItem psiItem) => GetCancellationResult();

	/// <inheritdoc />
	public HRESULT PostDeleteItem(uint dwFlags, IShellItem psiItem, HRESULT hrDelete, IShellItem psiNewlyCreated)
	{
		RecordItemResult(hrDelete);

		return GetCancellationResult();
	}

	/// <inheritdoc />
	public HRESULT PreNewItem(uint dwFlags, IShellItem psiDestinationFolder, PCWSTR pszNewName) => GetCancellationResult();

	/// <inheritdoc />
	public HRESULT PostNewItem(uint dwFlags, IShellItem psiDestinationFolder, PCWSTR pszNewName, PCWSTR pszTemplateName, uint dwFileAttributes, HRESULT hrNew, IShellItem psiNewItem)
	{
		RecordItemResult(hrNew);

		return GetCancellationResult();
	}

	/// <inheritdoc />
	public HRESULT UpdateProgress(uint iWorkTotal, uint iWorkSoFar)
	{
		var result = GetCancellationResult();
		if (result.Failed)
		{
			return result;
		}

		if (!_receivedByteProgress)
		{
			ReportEstimatedBytes(iWorkTotal, iWorkSoFar, iWorkTotal is not 0 && iWorkSoFar >= iWorkTotal);
		}

		return GetCancellationResult();
	}

	/// <inheritdoc />
	public HRESULT ResetTimer()
	{
		var now = Stopwatch.GetTimestamp();
		Interlocked.Exchange(ref _timerStartedTimestamp, now);
		Interlocked.Exchange(ref _pausedTimerDuration, 0);
		Interlocked.Exchange(ref _timerPausedTimestamp, 0);

		return HRESULT.S_OK;
	}

	/// <inheritdoc />
	public HRESULT PauseTimer()
	{
		PauseTimerCore();
		_operationControl?.AcknowledgePauseState(isPaused: true);

		return HRESULT.S_OK;
	}

	/// <inheritdoc />
	public HRESULT ResumeTimer()
	{
		ResumeTimerCore();
		_operationControl?.AcknowledgePauseState(isPaused: false);

		return HRESULT.S_OK;
	}

	/// <inheritdoc />
	public HRESULT GetMilliseconds(out ulong pullElapsed, out ulong pullRemaining)
	{
		var now = Stopwatch.GetTimestamp();
		var pausedAt = Volatile.Read(ref _timerPausedTimestamp);
		var effectiveNow = pausedAt is 0 ? now : pausedAt;
		var elapsedTimestamp = Math.Max(0, effectiveNow - Volatile.Read(ref _timerStartedTimestamp) - Volatile.Read(ref _pausedTimerDuration));
		pullElapsed = (ulong)(elapsedTimestamp * 1000d / Stopwatch.Frequency);
		pullRemaining = 0;

		return HRESULT.S_OK;
	}

	/// <inheritdoc />
	public unsafe HRESULT GetOperationStatus(PDOPSTATUS* popstatus)
	{
		if (popstatus == null)
		{
			return new HRESULT(PointerHResultValue);
		}

		if (_cancellationToken.IsCancellationRequested)
		{
			_operationControl?.AcknowledgeCancellationRequest();
			*popstatus = PDOPSTATUS.PDOPS_CANCELLED;

			return HRESULT.S_OK;
		}

		var isPauseRequested = _operationControl?.IsPauseRequested is true;
		*popstatus = isPauseRequested ? PDOPSTATUS.PDOPS_PAUSED : PDOPSTATUS.PDOPS_RUNNING;

		return HRESULT.S_OK;
	}

	private void PauseTimerCore()
	{
		_ = Interlocked.CompareExchange(ref _timerPausedTimestamp, Stopwatch.GetTimestamp(), 0);
	}

	private void ResumeTimerCore()
	{
		var pausedAt = Interlocked.Exchange(ref _timerPausedTimestamp, 0);
		if (pausedAt is not 0)
		{
			Interlocked.Add(ref _pausedTimerDuration, Math.Max(0, Stopwatch.GetTimestamp() - pausedAt));
		}
	}

	private HRESULT GetCancellationResult()
	{
		if (!_cancellationToken.IsCancellationRequested)
		{
			return HRESULT.S_OK;
		}

		_operationControl?.AcknowledgeCancellationRequest();

		return new HRESULT(CanceledHResultValue);
	}

	private void RecordItemResult(HRESULT result)
	{
		if (result.Failed)
		{
			_ = Interlocked.CompareExchange(ref _itemResult, result.Value, HRESULT.S_OK.Value);
		}
	}

	private void ReportEstimatedBytes(uint workTotal, uint workCompleted, bool force)
	{
		if (_totalBytes is not { } totalBytes)
		{
			return;
		}

		var completedBytes = workTotal is 0 ? 0 : (long)Math.Round(totalBytes * Math.Clamp((double)workCompleted / workTotal, 0, 1));
		ReportBytes((ulong)completedBytes, (ulong)totalBytes, force);
	}

	private void ReportBytes(ulong completedByteCount, ulong totalByteCount, bool force)
	{
		if (_progress is null)
		{
			return;
		}

		var totalBytes = totalByteCount is 0 ? _totalBytes : (long)Math.Min(totalByteCount, (ulong)long.MaxValue);
		if (totalBytes is not { } availableBytes)
		{
			return;
		}

		var now = Stopwatch.GetTimestamp();
		if (!force && Stopwatch.GetElapsedTime(_lastReportTimestamp, now) < ReportInterval)
		{
			return;
		}

		var completedBytes = (long)Math.Min(completedByteCount, (ulong)availableBytes);
		if (!force && completedBytes == _lastCompletedBytes)
		{
			return;
		}

		_lastReportTimestamp = now;
		_lastCompletedBytes = completedBytes;
		_progress.Report(new StorageOperationProgress(0, 1, _currentItem, completedBytes, availableBytes));
	}
}

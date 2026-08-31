// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.Infrastructure;

internal static class WslForegroundActivationGuard
{
	private static readonly TimeSpan _lockDuration = TimeSpan.FromSeconds(5);
	private static readonly Lock _syncRoot = new();
	private static CancellationTokenSource? _releaseCancellation;

	internal static void Protect(nint ownerWindowHandle)
	{
		var ownerWindow = (HWND)ownerWindowHandle;
		if (ownerWindow.IsNull || PInvoke.GetForegroundWindow() != ownerWindow)
		{
			return;
		}

		lock (_syncRoot)
		{
			if (!PInvoke.LockSetForegroundWindow(FOREGROUND_WINDOW_LOCK_CODE.LSFW_LOCK))
			{
				return;
			}

			var previousCancellation = _releaseCancellation;
			var releaseCancellation = new CancellationTokenSource();
			_releaseCancellation = releaseCancellation;
			previousCancellation?.Cancel();
			_ = ReleaseAsync(releaseCancellation);
		}
	}

	private static async Task ReleaseAsync(CancellationTokenSource releaseCancellation)
	{
		try
		{
			await Task.Delay(_lockDuration, releaseCancellation.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (releaseCancellation.IsCancellationRequested)
		{
			releaseCancellation.Dispose();

			return;
		}

		lock (_syncRoot)
		{
			if (!ReferenceEquals(_releaseCancellation, releaseCancellation))
			{
				releaseCancellation.Dispose();

				return;
			}

			_releaseCancellation = null;
			PInvoke.LockSetForegroundWindow(FOREGROUND_WINDOW_LOCK_CODE.LSFW_UNLOCK);
			releaseCancellation.Dispose();
		}
	}
}

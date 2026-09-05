// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Windows.Win32.System.Ole;
using Windows.Win32.UI.Shell;

namespace Files.Core.Windows;

internal sealed class WindowsShellEnumerationCancellationSite : IDisposable
{
	private readonly WindowsShellQueryContinue _queryContinue;

	private IObjectWithSite? _target;

	private WindowsShellEnumerationCancellationSite(IObjectWithSite target, WindowsShellQueryContinue queryContinue)
	{
		_target = target;
		_queryContinue = queryContinue;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		var target = Interlocked.Exchange(ref _target, null);
		if (target is null)
		{
			return;
		}

		target.SetSite(null!).ThrowOnFailure();
		GC.KeepAlive(_queryContinue);
	}

	internal static WindowsShellEnumerationCancellationSite? TryAttach(IEnumIDList enumerator, CancellationToken cancellationToken)
	{
		var target = enumerator as IObjectWithSite;
		if (target is null)
		{
			return null;
		}

		var queryContinue = new WindowsShellQueryContinue(cancellationToken);
		target.SetSite(queryContinue).ThrowOnFailure();

		return new WindowsShellEnumerationCancellationSite(target, queryContinue);
	}
}

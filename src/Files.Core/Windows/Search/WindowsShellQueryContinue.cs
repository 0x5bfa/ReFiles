// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace Files.Core.Windows;

[GeneratedComClass]
internal sealed partial class WindowsShellQueryContinue(CancellationToken cancellationToken) : IQueryContinue, IQueryContinueServiceProvider
{
	/// <inheritdoc />
	public HRESULT QueryContinue() => cancellationToken.IsCancellationRequested ? HRESULT.S_FALSE : HRESULT.S_OK;

	/// <inheritdoc />
	public HRESULT QueryService(in Guid serviceId, in Guid interfaceId, out IQueryContinue? service)
	{
		var queryContinueId = typeof(IQueryContinue).GUID;
		if (serviceId == queryContinueId && interfaceId == queryContinueId)
		{
			service = this;

			return HRESULT.S_OK;
		}

		service = null;

		return HRESULT.E_NOINTERFACE;
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.Core.Capabilities.Previews;

/// <summary>
/// Minimal in-process COM site exposed to a preview handler.
/// </summary>
[GeneratedComClass]
internal sealed unsafe partial class WindowsPreviewHandlerFrame : IPreviewHandlerFrame
{
	/// <inheritdoc />
	public HRESULT GetWindowContext(PREVIEWHANDLERFRAMEINFO* frameInfo)
	{
		if (frameInfo is null)
		{
			return HRESULT.E_POINTER;
		}

		*frameInfo = default;

		return HRESULT.S_OK;
	}

	/// <inheritdoc />
	public HRESULT TranslateAccelerator(MSG* message)
	{
		return HRESULT.S_FALSE;
	}
}

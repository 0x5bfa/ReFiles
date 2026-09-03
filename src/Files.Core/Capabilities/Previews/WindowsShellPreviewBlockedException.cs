// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities.Previews;

/// <summary>Indicates that a Shell preview became unsafe before handler activation.</summary>
public sealed class WindowsShellPreviewBlockedException : InvalidOperationException
{
	/// <summary>Gets the reason the preview was blocked.</summary>
	public PreviewBlockReason Reason { get; }

	/// <summary>Initializes a blocked Shell preview exception.</summary>
	/// <param name="reason">The reason the preview was blocked.</param>
	public WindowsShellPreviewBlockedException(PreviewBlockReason reason)
		: base($"The Windows Shell preview was blocked: {reason}.")
	{
		if (reason is not PreviewBlockReason.RequiresHydration and not PreviewBlockReason.TooLarge and not PreviewBlockReason.AccessDenied
			and not PreviewBlockReason.Untrusted and not PreviewBlockReason.DisabledByPolicy)
		{
			throw new ArgumentOutOfRangeException(nameof(reason));
		}

		Reason = reason;
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.ItemFeatures.Previews;

/// <summary>
/// Describes a Shell preview handler without owning Shell or UI resources.
/// </summary>
public sealed class WindowsShellPreviewResult : PreviewResult
{
	public WindowsShellPreviewResult(StorableReference reference, Guid handlerClsid)
	{
		ArgumentNullException.ThrowIfNull(reference);
		if (handlerClsid == Guid.Empty)
		{
			throw new ArgumentException("A preview handler CLSID is required.", nameof(handlerClsid));
		}

		Reference = reference;
		HandlerClsid = handlerClsid;
	}

	public StorableReference Reference { get; }

	public Guid HandlerClsid { get; }
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.ItemFeatures.Previews;

/// <summary>
/// Describes a Shell preview handler without owning Shell or UI resources.
/// </summary>
public sealed class WindowsShellPreviewResult : PreviewResult
{
	/// <summary>Gets the storage reference to preview.</summary>
	public StorableReference Reference { get; }

	/// <summary>Gets the preview handler CLSID.</summary>
	public Guid HandlerClsid { get; }

	/// <summary>Initializes a Windows Shell preview result.</summary>
	/// <param name="reference">The storage reference to preview.</param>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
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
}

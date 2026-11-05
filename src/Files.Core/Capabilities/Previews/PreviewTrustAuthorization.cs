// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;
using Files.Core.Storage;

namespace Files.Core.Capabilities.Previews;

internal sealed class PreviewTrustAuthorization : PreviewAuthorization
{
	private readonly StorableReference _reference;
	private readonly IStorable _coreModel;
	private readonly IPreviewTrustIdentity? _trustIdentity;

	internal PreviewTrustAuthorization(IStorableModel target)
	{
		ArgumentNullException.ThrowIfNull(target);

		_reference = target.Reference;
		_coreModel = target.GetCoreModel();
		_trustIdentity = (_coreModel as IPreviewTrustTarget)?.CaptureTrustIdentity();
	}

	internal PreviewTrustAuthorization(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		_reference = context.Reference;
		_coreModel = context.CoreModel;
		_trustIdentity = (context.CoreModel as IPreviewTrustTarget)?.CaptureTrustIdentity();
	}

	internal override bool AppliesTo(StorableReference reference, object storageItem)
	{
		if (!reference.Equals(_reference))
		{
			return false;
		}

		if (ReferenceEquals(storageItem, _coreModel))
		{
			return true;
		}

		return storageItem is IStorable item && _trustIdentity?.Matches(item) is true;
	}
}

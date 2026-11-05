// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Windows;

namespace Files.Core.Capabilities.Previews;

internal sealed class PreviewTrustAuthorization : PreviewAuthorization
{
	private readonly StorableReference _reference;
	private readonly IStorable _coreModel;
	private readonly string? _fileSystemPath;

	internal PreviewTrustAuthorization(IStorableModel target)
	{
		ArgumentNullException.ThrowIfNull(target);

		_reference = target.Reference;
		_coreModel = target.GetCoreModel();
		_fileSystemPath = (_coreModel as IWindowsStorable)?.FileSystemPath;
	}

	internal PreviewTrustAuthorization(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		_reference = context.Reference;
		_coreModel = context.CoreModel;
		_fileSystemPath = (context.CoreModel as IWindowsStorable)?.FileSystemPath;
	}

	internal bool AppliesTo(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (!context.Reference.Equals(_reference))
		{
			return false;
		}

		if (ReferenceEquals(context.CoreModel, _coreModel))
		{
			return true;
		}

		return _fileSystemPath is not null && context.CoreModel is IWindowsStorable windowsItem && StringComparer.OrdinalIgnoreCase.Equals(_fileSystemPath, windowsItem.FileSystemPath);
	}
}

internal static class PreviewAuthorizationExtensions
{
	internal static bool AppliesTo(this PreviewAuthorization authorization, ItemContext context)
	{
		return authorization is PreviewTrustAuthorization target && target.AppliesTo(context);
	}
}

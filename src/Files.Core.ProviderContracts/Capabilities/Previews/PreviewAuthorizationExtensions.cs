// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities.Previews;

internal static class PreviewAuthorizationExtensions
{
	internal static bool AppliesTo(this PreviewAuthorization authorization, ItemContext context)
	{
		return authorization.AppliesTo(context.Reference, context.CoreModel);
	}
}

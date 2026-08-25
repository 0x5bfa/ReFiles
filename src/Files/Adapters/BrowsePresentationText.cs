// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Localization;

namespace Files.Adapters;

internal sealed record BrowsePresentationText(string Home, string ItemCountSingle, string ItemCountPlural, string NotFolderFormat)
{
	public static BrowsePresentationText CreateLocalized()
	{
		return new BrowsePresentationText(
			Strings.Home.GetLocalized(),
			Strings.ItemCountSingle.GetLocalized(),
			Strings.ItemCountPlural.GetLocalized(),
			Strings.NotFolderFormat.GetLocalized());
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;

namespace Files.Core.Browsing;

/// <summary>Provides selection helpers for browse sessions.</summary>
public static class BrowseSessionExtensions
{
	/// <summary>Gets the focused item from the current snapshot.</summary>
	/// <param name="session">The browse session.</param>
	/// <returns>The focused item, or <see langword="null"/>.</returns>
	public static IStorableModel? GetFocusedItem(this IBrowseSession session)
	{
		ArgumentNullException.ThrowIfNull(session);

		return session.Selection.FocusedKey is { } key
			? session.Items.FirstOrDefault(item => item.Reference.GetKey() == key)
			: null;
	}

	/// <summary>Gets selected items from the current snapshot.</summary>
	/// <param name="session">The browse session.</param>
	/// <returns>The selected items in display order.</returns>
	public static IReadOnlyList<IStorableModel> GetSelectedItems(this IBrowseSession session)
	{
		ArgumentNullException.ThrowIfNull(session);

		var selectedKeys = session.Selection.SelectedKeys.ToHashSet();

		return Array.AsReadOnly(session.Items .Where(item => selectedKeys.Contains(item.Reference.GetKey())) .ToArray());
	}
}

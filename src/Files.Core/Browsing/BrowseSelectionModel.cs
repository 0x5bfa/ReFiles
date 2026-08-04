// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;

namespace Files.Core.Browsing;

internal sealed class BrowseSelectionModel
{
	private readonly Lock _lock = new();
	private BrowseSelectionState _state = BrowseSelectionState.Empty;

	internal BrowseSelectionState State => Volatile.Read(ref _state);

	internal bool Set(BrowseSelectionState nextState)
	{
		ArgumentNullException.ThrowIfNull(nextState);

		lock (_lock)
		{
			var currentState = Volatile.Read(ref _state);
			if (currentState.FocusedKey == nextState.FocusedKey && currentState.AnchorKey == nextState.AnchorKey && currentState.SelectedKeys.SequenceEqual(nextState.SelectedKeys))
			{
				return false;
			}

			Volatile.Write(ref _state, nextState);

			return true;
		}
	}

	internal bool Remove(StorableKey key)
	{
		lock (_lock)
		{
			var currentState = Volatile.Read(ref _state);
			if (!currentState.SelectedKeys.Contains(key) && currentState.FocusedKey != key && currentState.AnchorKey != key)
			{
				return false;
			}

			var nextState = new BrowseSelectionState(
				Array.AsReadOnly(currentState.SelectedKeys.Where(selectedKey => selectedKey != key).ToArray()),
				currentState.FocusedKey == key ? null : currentState.FocusedKey,
				currentState.AnchorKey == key ? null : currentState.AnchorKey);

			Volatile.Write(ref _state, nextState);

			return true;
		}
	}

	internal bool Migrate(StorableKey previousKey, StorableKey currentKey)
	{
		if (previousKey == currentKey)
		{
			return false;
		}

		lock (_lock)
		{
			var currentState = Volatile.Read(ref _state);
			var nextState = new BrowseSelectionState(
				Array.AsReadOnly(currentState.SelectedKeys.Select(selectedKey => selectedKey == previousKey ? currentKey : selectedKey).Distinct().ToArray()),
				currentState.FocusedKey == previousKey ? currentKey : currentState.FocusedKey,
				currentState.AnchorKey == previousKey ? currentKey : currentState.AnchorKey);
			if (currentState.FocusedKey == nextState.FocusedKey && currentState.AnchorKey == nextState.AnchorKey && currentState.SelectedKeys.SequenceEqual(nextState.SelectedKeys))
			{
				return false;
			}

			Volatile.Write(ref _state, nextState);

			return true;
		}
	}

	internal static BrowseSelectionState Normalize(BrowseSelectionState state, IReadOnlyList<IStorableModel> items)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(items);

		var existingKeys = items.Select(static item => item.Reference.GetKey()).ToHashSet();

		return new BrowseSelectionState(
			Array.AsReadOnly(state.SelectedKeys.Where(existingKeys.Contains).Distinct().ToArray()),
			state.FocusedKey is { } focusedKey && existingKeys.Contains(focusedKey) ? focusedKey : null,
			state.AnchorKey is { } anchorKey && existingKeys.Contains(anchorKey) ? anchorKey : null);
	}
}

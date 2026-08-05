// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;

namespace Files.Adapters;

internal abstract record BrowseItemViewModelChange;

internal sealed record BrowseItemViewModelAdded(int Index, BrowseItemViewModel Item) : BrowseItemViewModelChange;

internal sealed record BrowseItemViewModelRemoved(int Index) : BrowseItemViewModelChange;

internal sealed record BrowseItemViewModelReplaced(int Index, BrowseItemViewModel Item) : BrowseItemViewModelChange;

internal sealed record BrowseItemViewModelMoved(int PreviousIndex, int CurrentIndex) : BrowseItemViewModelChange;

internal sealed record BrowseItemViewModelsReset(IReadOnlyList<BrowseItemViewModel> Items) : BrowseItemViewModelChange;

internal sealed class CoreBrowseUpdatedEventArgs(IReadOnlyList<BrowseItemViewModelChange> itemChanges, bool selectionChanged) : EventArgs
{
	public IReadOnlyList<BrowseItemViewModelChange> ItemChanges { get; } =
		itemChanges;

	public bool SelectionChanged { get; } = selectionChanged;
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;

namespace Files.Adapters;

internal abstract record BrowseItemViewModelChange;

internal sealed record BrowseItemViewModelAdded(int Index, BrowseItemViewModel Item) : BrowseItemViewModelChange;

internal sealed record BrowseItemViewModelsAdded(int StartingIndex, IReadOnlyList<BrowseItemViewModel> Items) : BrowseItemViewModelChange;

internal sealed record BrowseItemViewModelRemoved(int Index) : BrowseItemViewModelChange;

internal sealed record BrowseItemViewModelReplaced(int Index, BrowseItemViewModel Item) : BrowseItemViewModelChange;

internal sealed record BrowseItemViewModelMoved(int PreviousIndex, int CurrentIndex) : BrowseItemViewModelChange;

internal sealed record BrowseItemViewModelsReset(IReadOnlyList<BrowseItemViewModel> Items) : BrowseItemViewModelChange;

[Flags]
internal enum BrowseUpdateFlags
{
	None = 0,
	Items = 1 << 0,
	Location = 1 << 1,
	Loading = 1 << 2,
	NavigationCapabilities = 1 << 3,
	Columns = 1 << 4,
	Selection = 1 << 5,
	Status = 1 << 6,
	ViewSettings = 1 << 7,
}

internal sealed class CoreBrowseUpdatedEventArgs(IReadOnlyList<BrowseItemViewModelChange> itemChanges, BrowseUpdateFlags flags) : EventArgs
{
	public IReadOnlyList<BrowseItemViewModelChange> ItemChanges { get; } =
		itemChanges;

	public BrowseUpdateFlags Flags { get; } = flags;

	public bool SelectionChanged => Flags.HasFlag(BrowseUpdateFlags.Selection);
}

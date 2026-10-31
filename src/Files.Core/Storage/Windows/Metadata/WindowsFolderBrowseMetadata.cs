// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Models;
using Files.Core.ViewSettings;

namespace Files.Core.Storage.Windows;

internal sealed class WindowsFolderBrowseMetadata(WindowsFolder folder, IWindowsShellScheduler scheduler) : IFolderBrowseMetadata
{
	private readonly WindowsFolder _folder = folder;
	private readonly IWindowsShellScheduler _scheduler = scheduler;

	/// <inheritdoc />
	public async ValueTask<BrowseColumnSet?> GetColumnsAsync(CancellationToken cancellationToken = default)
	{
		var columns = await _scheduler.InvokeAsync(() =>
		{
			var shellItem = WindowShellHelpers.TryResolveItem(_folder.Locator);

			return shellItem is null ? null : WindowsShellColumnReader.Read(shellItem, _folder.ParsingName, cancellationToken);
		}, cancellationToken).ConfigureAwait(false);
		if (columns is null)
		{
			return null;
		}

		var mapped = columns.All.Select(static column => new BrowseColumn(column.Index, column.PropertyId, column.DisplayName,
			column.HeaderWidthCharacters > 0 ? column.HeaderWidthCharacters * 8d : 120d, (BrowseColumnAlignment)column.Alignment,
			column.IsVisibleByDefault, column.IsHidden, canGroup: column.CanGroup, isFixedWidth: column.IsFixedWidth));

		return new BrowseColumnSet(mapped, columns.DefaultSortColumnIndex);
	}

	/// <inheritdoc />
	public async ValueTask<IReadOnlyList<IStorableModel>?> SortItemsAsync(IReadOnlyList<IStorableModel> items, BrowseViewSettings settings, CancellationToken cancellationToken)
	{
		var nativeItems = new WindowsStorable[items.Count];
		var modelsByItem = new Dictionary<WindowsStorable, IStorableModel>(ReferenceEqualityComparer.Instance);
		for (var index = 0; index < items.Count; index++)
		{
			if (items[index].GetCoreModel() is not WindowsStorable item)
			{
				return null;
			}

			nativeItems[index] = item;
			modelsByItem.Add(item, items[index]);
		}

		var sorted = await WindowsShellItemSorter.SortAsync(_scheduler, _folder.Descriptor, nativeItems, settings.SortPropertyId, settings.SortDirection, cancellationToken).ConfigureAwait(false);

		return sorted is null ? null : Array.AsReadOnly(sorted.Select(item => modelsByItem[item]).ToArray());
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Files.Core.ViewSettings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Windows;

internal static unsafe class WindowsShellItemSorter
{
	internal static Task<IReadOnlyList<WindowsStorable>?> SortAsync(
		WindowsShellItemResolver resolver,
		WindowsStorableDescriptor descriptor,
		IReadOnlyList<WindowsStorable> items,
		string? propertyId,
		ViewSortDirection direction,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(resolver);
		ArgumentNullException.ThrowIfNull(descriptor);
		ArgumentNullException.ThrowIfNull(items);

		if (items.Count < 2)
		{
			return Task.FromResult<IReadOnlyList<WindowsStorable>?>(Array.AsReadOnly(items.ToArray()));
		}

		foreach (var item in items)
		{
			var parentFolder = item.Locator.ParentFolder;
			if (parentFolder is null || item.Locator.RelativePidl.IsEmpty || !parentFolder.AbsolutePidl.Span.SequenceEqual(descriptor.Locator.AbsolutePidl.Span))
			{
				return Task.FromResult<IReadOnlyList<WindowsStorable>?>(null);
			}
		}

		return resolver.InvokeConcurrentAsync<IReadOnlyList<WindowsStorable>?>(
			descriptor.Locator,
			shellItem => SortOnCurrentSta(shellItem, descriptor.Locator.ParsingName, items, propertyId, direction, cancellationToken),
			cancellationToken);
	}

	private static IReadOnlyList<WindowsStorable>? SortOnCurrentSta(
		IShellItem shellItem,
		string parsingName,
		IReadOnlyList<WindowsStorable> items,
		string? propertyId,
		ViewSortDirection direction,
		CancellationToken cancellationToken)
	{
		var folder = WindowsShellColumnReader.TryGetFolder(shellItem, parsingName, cancellationToken);
		if (folder is null)
		{
			return null;
		}

		var isNameProperty = string.IsNullOrWhiteSpace(propertyId) || propertyId.Equals("name", StringComparison.OrdinalIgnoreCase);
		var effectivePropertyId = isNameProperty ? "System.ItemNameDisplay" : propertyId!;
		var columnIndex = WindowsShellColumnReader.FindColumnIndex(folder, effectivePropertyId, cancellationToken);
		if (columnIndex is null)
		{
			return null;
		}

		var sortedFolders = items.Where(static item => item.Snapshot.IsFolder).ToArray();
		var sortedFiles = items.Where(static item => !item.Snapshot.IsFolder).ToArray();
		try
		{
			Array.Sort(sortedFolders, (left, right) => CompareChildren(folder, columnIndex.Value, direction, left, right, cancellationToken));
			Array.Sort(sortedFiles, (left, right) => CompareChildren(folder, columnIndex.Value, direction, left, right, cancellationToken));
		}
		catch (InvalidOperationException exception) when (exception.InnerException is OperationCanceledException && cancellationToken.IsCancellationRequested)
		{
			cancellationToken.ThrowIfCancellationRequested();
			throw;
		}

		var sortedItems = sortedFolders.Concat(sortedFiles).ToArray();

		return Array.AsReadOnly(sortedItems);
	}

	private static int CompareChildren(IShellFolder2 folder, int columnIndex, ViewSortDirection direction, WindowsStorable left, WindowsStorable right, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		fixed (byte* leftPidlBytes = left.Locator.RelativePidl.Span)
		fixed (byte* rightPidlBytes = right.Locator.RelativePidl.Span)
		{
			var result = folder.CompareIDs(new LPARAM(columnIndex), in *(ITEMIDLIST*)leftPidlBytes, in *(ITEMIDLIST*)rightPidlBytes);
			var comparison = result.Succeeded ? unchecked((short)(result.Value & ushort.MaxValue)) : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);

			return direction is ViewSortDirection.Ascending ? comparison : -comparison;
		}
	}
}

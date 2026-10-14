// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Buffers.Binary;
using System.IO;
using Files.Core.Storage;

namespace Files.Core.Windows;

internal static class WindowsShellMenuSelectionResolver
{
	internal static bool HasCommonParent(IReadOnlyList<WindowsItemLocator> locators)
	{
		if (locators.Count is 0 || !TryGetParentPidl(locators[0].AbsolutePidl.Span, out var firstParent))
		{
			return false;
		}

		for (var index = 1; index < locators.Count; index++)
		{
			if (!TryGetParentPidl(locators[index].AbsolutePidl.Span, out var parent) || !firstParent.SequenceEqual(parent))
			{
				return false;
			}
		}

		return true;
	}

	private static bool TryGetParentPidl(ReadOnlySpan<byte> absolutePidl, out ReadOnlySpan<byte> parent)
	{
		parent = default;
		var offset = 0;
		var lastItemOffset = -1;
		while (offset + sizeof(ushort) <= absolutePidl.Length)
		{
			var itemSize = BinaryPrimitives.ReadUInt16LittleEndian(absolutePidl[offset..]);
			if (itemSize is 0)
			{
				if (lastItemOffset < 0 || offset + sizeof(ushort) != absolutePidl.Length)
				{
					return false;
				}

				parent = absolutePidl[..lastItemOffset];

				return true;
			}

			if (itemSize < sizeof(ushort) || offset + itemSize > absolutePidl.Length)
			{
				return false;
			}

			lastItemOffset = offset;
			offset += itemSize;
		}

		return false;
	}

	internal static async Task<WindowsShellResolvedSelection?> ResolveAsync(WindowsStorageSource source, IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken)
	{
		var locators = new List<WindowsItemLocator>(selection.Count);
		var fileSystemPaths = new List<string>(selection.Count);
		WindowsStorable? firstItem = null;
		HashSet<string>? itemTypes = null;
		foreach (var reference in selection)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (reference.SourceId != source.SourceId || await source.ResolveAsync(reference, cancellationToken).ConfigureAwait(false) is not WindowsStorable item)
			{
				return null;
			}

			firstItem ??= item;
			if (itemTypes is null)
			{
				itemTypes = new(GetItemTypes(item), StringComparer.OrdinalIgnoreCase);
			}
			else
			{
				itemTypes.IntersectWith(GetItemTypes(item));
			}

			locators.Add(item.Locator);
			if (item.FileSystemPath is { } fileSystemPath)
			{
				fileSystemPaths.Add(fileSystemPath);
			}
		}

		return firstItem is null ? null : new(locators.ToArray(), itemTypes!.ToArray(), fileSystemPaths.ToArray(), selection.Count is 1 && firstItem is WindowsFolder);
	}

	private static IReadOnlyList<string> GetItemTypes(WindowsStorable firstItem)
	{
		if (firstItem is WindowsFolder)
		{
			return ["Directory"];
		}

		var extension = Path.GetExtension(firstItem.Name);

		return string.IsNullOrEmpty(extension) ? ["*"] : [extension.ToLowerInvariant(), "*"];
	}

}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Files.Core.Storage;
using OwlCore.Storage;

namespace Files.Core.Windows;

internal static class WindowsShellSelectionResolver
{
	internal static async Task<IReadOnlyList<WindowsItemLocator>> ResolveAsync(WindowsStorageSource source, IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(source);

		ArgumentNullException.ThrowIfNull(selection);

		var locators = new List<WindowsItemLocator>(selection.Count);
		foreach (var reference in selection)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (reference.SourceId != source.SourceId)
			{
				throw new ArgumentException($"Reference belongs to storage source '{reference.SourceId}'.", nameof(selection));
			}

			if (await source.ResolveAsync(reference, cancellationToken).ConfigureAwait(false) is not WindowsStorable item)
			{
				throw new InvalidOperationException("The Shell selection contains an item that is not backed by Windows.");
			}

			locators.Add(item.Locator);
		}

		return locators;
	}
}

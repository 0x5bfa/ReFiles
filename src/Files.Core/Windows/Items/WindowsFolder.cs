// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.CompilerServices;
using Files.Core.ViewSettings;
using OwlCore.Storage;
using Windows.Win32.Foundation;

namespace Files.Core.Windows;

/// <summary>Represents a folder exposed by the Windows Shell.</summary>
public sealed class WindowsFolder : WindowsStorable, IChildFolder
{
	internal WindowsFolder(WindowsStorableDescriptor descriptor, WindowsStorableFactory factory)
		: base(descriptor, factory)
	{
	}

	/// <summary>
	/// Gets the column metadata exposed by this Shell folder.
	/// </summary>
	/// <param name="cancellationToken">The token used to cancel the Shell operation.</param>
	/// <returns>The Shell columns and the columns enabled by default.</returns>
	public Task<WindowsShellColumnSet> GetColumnsAsync(CancellationToken cancellationToken = default)
	{
		return Factory.GetColumnsAsync(Descriptor, cancellationToken);
	}

	/// <summary>Gets view settings persisted in the Windows Shell store for this folder.</summary>
	/// <param name="cancellationToken">The token used to cancel the Shell operation.</param>
	/// <returns>The persisted Shell settings, or <see langword="null"/> when no settings are stored.</returns>
	public Task<BrowseViewSettingsOverride?> GetViewSettingsAsync(CancellationToken cancellationToken = default)
	{
		return Factory.GetViewSettingsAsync(Descriptor, cancellationToken);
	}

	/// <summary>Persists supported view settings in the Windows Shell store for this folder.</summary>
	/// <param name="settingsOverride">The requested settings.</param>
	/// <param name="cancellationToken">The token used to cancel the Shell operation.</param>
	/// <returns>The resulting Shell state and application-owned remainder.</returns>
	public Task<ViewSettingsPersistenceResult> SetViewSettingsAsync(BrowseViewSettingsOverride settingsOverride, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(settingsOverride);

		return Factory.SetViewSettingsAsync(Descriptor, settingsOverride, cancellationToken);
	}

	/// <summary>Clears selected view settings from the Windows Shell store for this folder.</summary>
	/// <param name="fields">The settings to clear.</param>
	/// <param name="cancellationToken">The token used to cancel the Shell operation.</param>
	/// <returns>The remaining Shell settings.</returns>
	public Task<BrowseViewSettingsOverride> ClearViewSettingsAsync(ViewSettingsOverrideFields fields, CancellationToken cancellationToken = default)
	{
		return Factory.ClearViewSettingsAsync(Descriptor, fields, cancellationToken);
	}

	/// <summary>Enumerates the items in the Windows Shell folder.</summary>
	/// <param name="type">The kinds of items to include.</param>
	/// <param name="cancellationToken">The token used to cancel enumeration.</param>
	/// <returns>An asynchronous sequence of child items.</returns>
	public async IAsyncEnumerable<IStorableChild> GetItemsAsync(StorableType type = StorableType.All, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await foreach (var item in GetItemsAsync(type, 0, cancellationToken).ConfigureAwait(false))
		{
			yield return item;
		}
	}

	internal async IAsyncEnumerable<IStorableChild> GetItemsAsync(StorableType type, nint ownerWindowHandle, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (type is StorableType.None)
		{
			yield break;
		}

		await foreach (var descriptor in Factory.EnumerateChildrenAsync(Descriptor, new HWND(ownerWindowHandle), cancellationToken).ConfigureAwait(false))
		{
			cancellationToken.ThrowIfCancellationRequested();

			var include = descriptor.Snapshot.IsFolder
				? type.HasFlag(StorableType.Folder)
				: type.HasFlag(StorableType.File);

			if (include)
			{
				yield return Factory.Create(descriptor);
			}
		}
	}

	internal Task<IReadOnlyList<WindowsStorable>?> SortChildrenAsync(IReadOnlyList<WindowsStorable> items, string? propertyId, ViewSortDirection direction, CancellationToken cancellationToken)
	{
		return WindowsShellItemSorter.SortAsync(Factory.Resolver, Descriptor, items, propertyId, direction, cancellationToken);
	}
}

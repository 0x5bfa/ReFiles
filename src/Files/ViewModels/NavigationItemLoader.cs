// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Data;
using Files.Core.ItemFeatures;
using Files.Core.ItemFeatures.Properties;
using Files.Core.ItemFeatures.Thumbnails;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
using Files.Localization;
using OwlCore.Storage;
using Windows.Win32;

namespace Files.ViewModels;

internal sealed class NavigationItemLoader
{
	private const string PinnedParsingName =
		"shell:::{3936E9E4-D92C-4EEE-A85A-BC16D5EA0819}";
	private const string NetworkParsingName =
		"shell:::{208D2C60-3AEA-1069-A2D7-08002B30309D}";
	private const string WslParsingName =
		"shell:::{B2B4A4D1-2754-4140-A2EB-9A76D9D7CDC6}";
	private const string HomeIsPinned = "System.Home.IsPinned";
	private static readonly string MyComputerParsingName =
		$"shell:::{CLSID.CLSID_MyComputer:B}";

	private const int ThumbnailSize = 20;

	private readonly IFilesDataRoot dataRoot;

	public NavigationItemLoader(IFilesDataRoot dataRoot)
	{
		ArgumentNullException.ThrowIfNull(dataRoot);
		this.dataRoot = dataRoot;
	}

	public async Task<IReadOnlyList<NavigationSectionData>> LoadAsync(
		CancellationToken cancellationToken = default)
	{
		var windowsSource = dataRoot.Sources
			.OfType<WindowsStorageSource>()
			.FirstOrDefault();
		if (windowsSource is null)
		{
			return [];
		}

		var sections = new List<NavigationSectionData>();
		var pinned = await TryLoadAddressSectionAsync(
			windowsSource,
			Strings.Pinned.GetLocalized(),
			PinnedParsingName,
			IsPinnedHomeItemAsync,
			cancellationToken).ConfigureAwait(false);
		if (pinned is not null)
		{
			sections.Add(pinned);
		}

		var drives = await TryLoadAddressSectionAsync(
			windowsSource,
			Strings.Drives.GetLocalized(),
			MyComputerParsingName,
			static (_, _) => ValueTask.FromResult(true),
			cancellationToken).ConfigureAwait(false);
		if (drives is not null)
		{
			sections.Add(drives);
		}

		var network = await TryLoadAddressSectionAsync(
			windowsSource,
			Strings.Network.GetLocalized(),
			NetworkParsingName,
			static (_, _) => ValueTask.FromResult(true),
			cancellationToken).ConfigureAwait(false);
		if (network is not null)
		{
			sections.Add(network);
		}

		var wsl = await TryLoadAddressSectionAsync(
			windowsSource,
			Strings.WSL.GetLocalized(),
			WslParsingName,
			static (_, _) => ValueTask.FromResult(true),
			cancellationToken).ConfigureAwait(false);
		if (wsl is not null)
		{
			sections.Add(wsl);
		}

		return sections;
	}

	private async Task<NavigationSectionData?> TryLoadAddressSectionAsync(
		WindowsStorageSource source,
		string name,
		string parsingName,
		Func<IStorableModel, CancellationToken, ValueTask<bool>> include,
		CancellationToken cancellationToken)
	{
		try
		{
			var model = await dataRoot.ResolveAsync(
				source.SourceId,
				new StorageAddress(
					WindowsStorageSource.ShellAddressScheme,
					parsingName),
				cancellationToken).ConfigureAwait(false);

			if (model is not IFolderModel folder)
			{
				await model.DisposeAsync().ConfigureAwait(false);
				return null;
			}

			return await LoadSectionAsync(
				name,
				folder,
				include,
				cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static async Task<NavigationSectionData> LoadSectionAsync(
		string name,
		IFolderModel folder,
		Func<IStorableModel, CancellationToken, ValueTask<bool>> include,
		CancellationToken cancellationToken)
	{
		try
		{
			var children = new List<NavigationItemData>();
			await foreach (var item in folder
				.GetItemsAsync(StorableType.Folder, cancellationToken)
				.ConfigureAwait(false))
			{
				try
				{
					if (await include(item, cancellationToken).ConfigureAwait(false))
					{
						children.Add(
							new NavigationItemData(
								item.Name,
								item.Reference,
								await GetThumbnailAsync(item, cancellationToken)
									.ConfigureAwait(false)));
					}
				}
				finally
				{
					await item.DisposeAsync().ConfigureAwait(false);
				}
			}

			return new NavigationSectionData(
				name,
				folder.Reference,
				children);
		}
		finally
		{
			await folder.DisposeAsync().ConfigureAwait(false);
		}
	}

	private static async ValueTask<bool> IsPinnedHomeItemAsync(
		IStorableModel item,
		CancellationToken cancellationToken)
	{
		if (item.Get<IPropertySource>() is not { } propertySource)
		{
			return false;
		}

		try
		{
			var properties = await propertySource
				.GetPropertiesAsync(
					new PropertyRequest([HomeIsPinned]),
					cancellationToken)
				.ConfigureAwait(false);
			return properties.TryGetValue(
				HomeIsPinned,
				out var value)
				&& value is bool isPinned
				&& isPinned;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static async ValueTask<byte[]?> GetThumbnailAsync(
		IStorableModel item,
		CancellationToken cancellationToken)
	{
		if (item.Get<IThumbnailSource>() is not { } source)
		{
			return null;
		}

		try
		{
			var result = await source
				.GetThumbnailAsync(
					new ThumbnailRequest(
						ThumbnailSize,
						ThumbnailMode.Icon),
					cancellationToken)
				.ConfigureAwait(false);
			return result?.Content.ToArray();
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception)
		{
			return null;
		}
	}
}

internal sealed record NavigationSectionData(
	string Name,
	StorableReference Reference,
	IReadOnlyList<NavigationItemData> Items);

internal sealed record NavigationItemData(
	string Name,
	StorableReference Reference,
	byte[]? Thumbnail);

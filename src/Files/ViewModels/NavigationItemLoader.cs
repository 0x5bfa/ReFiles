// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
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
	private const string PinnedParsingName = "shell:::{3936E9E4-D92C-4EEE-A85A-BC16D5EA0819}";

	private const string NetworkParsingName = "shell:::{208D2C60-3AEA-1069-A2D7-08002B30309D}";

	private const string WslParsingName = "shell:::{B2B4A4D1-2754-4140-A2EB-9A76D9D7CDC6}";

	private const string HomeIsPinned = "System.Home.IsPinned";

	private const int MaxConcurrentItemLoads = 4;

	private const int ThumbnailSize = 16;

	private static readonly string _myComputerParsingName = $"shell:::{CLSID.CLSID_MyComputer:B}";

	private readonly IFilesDataRoot _dataRoot;

	public NavigationItemLoader(IFilesDataRoot dataRoot)
	{
		ArgumentNullException.ThrowIfNull(dataRoot);

		_dataRoot = dataRoot;
	}

	public async IAsyncEnumerable<NavigationSectionData> LoadSectionsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var windowsSource = _dataRoot.Sources.OfType<WindowsStorageSource>().FirstOrDefault();
		if (windowsSource is null)
		{
			yield break;
		}

		var pendingSections = new[]
		{
			TryLoadAddressSectionAsync(0, windowsSource, Strings.Pinned.GetLocalized(), PinnedParsingName, IsPinnedHomeItemAsync, cancellationToken),
			TryLoadAddressSectionAsync(1, windowsSource, Strings.Drives.GetLocalized(), _myComputerParsingName, static (_, _) => ValueTask.FromResult(true), cancellationToken),
			TryLoadAddressSectionAsync(2, windowsSource, Strings.Network.GetLocalized(), NetworkParsingName, static (_, _) => ValueTask.FromResult(true), cancellationToken),
			TryLoadAddressSectionAsync(3, windowsSource, Strings.WSL.GetLocalized(), WslParsingName, static (_, _) => ValueTask.FromResult(true), cancellationToken),
		};

		while (pendingSections.Length > 0)
		{
			var completed = await Task.WhenAny(pendingSections).ConfigureAwait(false);
			pendingSections = [.. pendingSections.Where(task => !ReferenceEquals(task, completed))];

			if (await completed.ConfigureAwait(false) is { } section)
			{
				yield return section;
			}
		}
	}

	public async ValueTask<byte[]?> LoadThumbnailAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		var model = await _dataRoot.ResolveAsync(reference, cancellationToken).ConfigureAwait(false);
		try
		{
			if (model.Get<IThumbnailSource>() is not { } source)
			{
				return null;
			}

			var result = await source.GetThumbnailAsync(new ThumbnailRequest(ThumbnailSize, ThumbnailMode.PreferContent), cancellationToken).ConfigureAwait(false);

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
		finally
		{
			await model.DisposeAsync().ConfigureAwait(false);
		}
	}

	private async Task<NavigationSectionData?> TryLoadAddressSectionAsync(int order, WindowsStorageSource source, string name, string parsingName, Func<IStorableModel, CancellationToken, ValueTask<bool>> include, CancellationToken cancellationToken)
	{
		try
		{
			var model = await _dataRoot.ResolveAsync(source.SourceId, new StorageAddress(WindowsStorageSource.ShellAddressScheme, parsingName), cancellationToken).ConfigureAwait(false);
			if (model is not IFolderModel folder)
			{
				await model.DisposeAsync().ConfigureAwait(false);

				return null;
			}

			var pendingItems = new List<Task<NavigationItemData?>>(MaxConcurrentItemLoads);

			try
			{
				var children = new List<NavigationItemData>();
				await foreach (var item in folder.GetItemsAsync(StorableType.Folder, cancellationToken).ConfigureAwait(false))
				{
					pendingItems.Add(LoadItemAsync(item, include, cancellationToken));
					if (pendingItems.Count < MaxConcurrentItemLoads)
					{
						continue;
					}

					var loadedItems = await Task.WhenAll(pendingItems).ConfigureAwait(false);
					foreach (var loadedItem in loadedItems)
					{
						if (loadedItem is not null)
						{
							children.Add(loadedItem);
						}
					}

					pendingItems.Clear();
				}

				if (pendingItems.Count > 0)
				{
					var loadedItems = await Task.WhenAll(pendingItems).ConfigureAwait(false);
					foreach (var loadedItem in loadedItems)
					{
						if (loadedItem is not null)
						{
							children.Add(loadedItem);
						}
					}

					pendingItems.Clear();
				}

				return new NavigationSectionData(order, name, folder.Reference, children);
			}
			finally
			{
				try
				{
					if (pendingItems.Count > 0)
					{
						await Task.WhenAll(pendingItems).ConfigureAwait(false);
					}
				}
				catch (Exception)
				{
					// Preserve the original enumeration error.
				}

				await folder.DisposeAsync().ConfigureAwait(false);
			}
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

	private static async Task<NavigationItemData?> LoadItemAsync(IStorableModel item, Func<IStorableModel, CancellationToken, ValueTask<bool>> include, CancellationToken cancellationToken)
	{
		try
		{
			return await include(item, cancellationToken).ConfigureAwait(false)
				? new NavigationItemData(item.Name, item.Reference)
				: null;
		}
		finally
		{
			await item.DisposeAsync().ConfigureAwait(false);
		}
	}

	private static async ValueTask<bool> IsPinnedHomeItemAsync(IStorableModel item, CancellationToken cancellationToken)
	{
		if (item.Get<IPropertySource>() is not { } propertySource)
		{
			return false;
		}

		try
		{
			var properties = await propertySource.GetPropertiesAsync(new PropertyRequest([HomeIsPinned]), cancellationToken).ConfigureAwait(false);

			return properties.TryGetValue(HomeIsPinned, out var value) && value is bool isPinned && isPinned;
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
}

internal sealed record NavigationSectionData(int Order, string Name, StorableReference Reference, IReadOnlyList<NavigationItemData> Items);

internal sealed record NavigationItemData(string Name, StorableReference Reference);

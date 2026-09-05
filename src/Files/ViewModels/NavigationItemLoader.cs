// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Files.Core.Data;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Properties;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Infrastructure;
using Files.Localization;
using Files.Core.Windows;
using OwlCore.Storage;
using Windows.Storage.Provider;
using Windows.Win32;

namespace Files.ViewModels;

internal sealed class NavigationItemLoader
{
	private const string PinnedParsingName = "shell:::{3936E9E4-D92C-4EEE-A85A-BC16D5EA0819}";

	private const string DesktopParsingName = "shell:Desktop";

	private const string WslParsingName = "shell:::{B2B4A4D1-2754-4140-A2EB-9A76D9D7CDC6}";

	private const string HomeIsPinned = "System.Home.IsPinned";

	private const int MaxConcurrentItemLoads = 4;

	private const int ThumbnailSize = 16;

	private static readonly string _myComputerParsingName = $"shell:::{CLSID.CLSID_MyComputer:B}";

	private readonly IStorageWorkspace _workspace;

	public NavigationItemLoader(IStorageWorkspace workspace)
	{
		ArgumentNullException.ThrowIfNull(workspace);

		_workspace = workspace;
	}

	public async IAsyncEnumerable<NavigationSectionData> LoadSectionsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var startTimestamp = Stopwatch.GetTimestamp();
		UiDiagnosticLog.Write("NavigationItemLoader", "LoadSections START");
		var windowsSource = _workspace.Sources.OfType<WindowsStorageSource>().FirstOrDefault();
		if (windowsSource is null)
		{
			UiDiagnosticLog.Write("NavigationItemLoader", "LoadSections END no Windows source");

			yield break;
		}

		var pendingSections = new[]
		{
			TryLoadAddressSectionAsync(0, SidebarSectionType.Pinned, windowsSource, Strings.Pinned.GetLocalized(), PinnedParsingName, IsPinnedHomeItemAsync, cancellationToken),
			TryLoadAddressSectionAsync(1, SidebarSectionType.Drives, windowsSource, Strings.Drives.GetLocalized(), _myComputerParsingName, static (_, _) => ValueTask.FromResult(true), cancellationToken),
			TryLoadCloudDrivesSectionAsync(2, windowsSource, cancellationToken),
			TryLoadAddressSectionAsync(3, SidebarSectionType.WSL, windowsSource, Strings.WSL.GetLocalized(), WslParsingName, static (_, _) => ValueTask.FromResult(true), cancellationToken),
		};

		while (pendingSections.Length > 0)
		{
			var completed = await Task.WhenAny(pendingSections).ConfigureAwait(false);
			pendingSections = [.. pendingSections.Where(task => !ReferenceEquals(task, completed))];

			if (await completed.ConfigureAwait(false) is { } section)
			{
				UiDiagnosticLog.Write(
					"NavigationItemLoader",
					$"LoadSections yielded order={section.Order} name={section.Name} items={section.Items.Count} elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");
				yield return section;
			}
		}

		UiDiagnosticLog.Write("NavigationItemLoader", $"LoadSections END elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");
	}

	public async ValueTask<ThumbnailResult?> LoadThumbnailAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		var startTimestamp = Stopwatch.GetTimestamp();
		UiDiagnosticLog.Write("NavigationItemLoader", $"LoadThumbnail START id={reference.ItemId}");
		var model = await _workspace.ResolveAsync(reference, cancellationToken).ConfigureAwait(false);
		try
		{
			if (model.Get<IThumbnailSource>() is not { } source)
			{
				return null;
			}

			var result = await source.GetThumbnailAsync(new ThumbnailRequest(ThumbnailSize, ThumbnailMode.Icon), cancellationToken).ConfigureAwait(false);
			UiDiagnosticLog.Write(
				"NavigationItemLoader",
				$"LoadThumbnail END id={reference.ItemId} bytes={result?.Content.Length ?? 0} elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");

			return result;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception)
		{
			UiDiagnosticLog.Write("NavigationItemLoader", $"LoadThumbnail ERROR id={reference.ItemId} elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");
			return null;
		}
		finally
		{
			await model.DisposeAsync().ConfigureAwait(false);
		}
	}

	private async Task<NavigationSectionData?> TryLoadAddressSectionAsync(
		int order,
		SidebarSectionType sectionType,
		WindowsStorageSource source,
		string name,
		string parsingName,
		Func<IStorableModel, CancellationToken, ValueTask<bool>> include,
		CancellationToken cancellationToken)
	{
		var startTimestamp = Stopwatch.GetTimestamp();
		UiDiagnosticLog.Write("NavigationItemLoader", $"Section START order={order} name={name} parsingName={parsingName}");
		try
		{
			var model = await _workspace.ResolveAsync(source.SourceId, new StorageAddress(WindowsStorageSource.ShellAddressScheme, parsingName), cancellationToken).ConfigureAwait(false);
			if (model is not IFolderModel folder)
			{
				await model.DisposeAsync().ConfigureAwait(false);
				UiDiagnosticLog.Write("NavigationItemLoader", $"Section END order={order} notFolder elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");

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

				UiDiagnosticLog.Write("NavigationItemLoader", $"Section END order={order} items={children.Count} elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");

				return new NavigationSectionData(order, sectionType, name, folder.Reference, children);
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
		catch (Exception exception)
		{
			UiDiagnosticLog.Write(
				"NavigationItemLoader",
				$"Section ERROR order={order} type={exception.GetType().Name} message={exception.Message} elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");

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

	private async Task<NavigationSectionData?> TryLoadCloudDrivesSectionAsync(int order, WindowsStorageSource source, CancellationToken cancellationToken)
	{
		HashSet<string> syncRootPaths;
		try
		{
			syncRootPaths = StorageProviderSyncRootManager.GetCurrentSyncRoots()
				.Select(static root => root.Path?.Path)
				.Where(static path => !string.IsNullOrWhiteSpace(path))
				.Select(static path => Path.TrimEndingDirectorySeparator(path!))
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
		}
		catch (Exception exception)
		{
			UiDiagnosticLog.Write("NavigationItemLoader", $"Cloud roots ERROR type={exception.GetType().Name} message={exception.Message}");

			return null;
		}

		if (syncRootPaths.Count is 0)
		{
			return null;
		}

		var section = await TryLoadAddressSectionAsync(
			order,
			SidebarSectionType.CloudDrives,
			source,
			Strings.CloudDrives.GetLocalized(),
			DesktopParsingName,
			(item, _) => ValueTask.FromResult(IsCloudDrive(item, syncRootPaths)),
			cancellationToken).ConfigureAwait(false);

		return section is { Items.Count: > 0 } ? section : null;
	}

	private static bool IsCloudDrive(IStorableModel item, IReadOnlySet<string> syncRootPaths)
	{
		var address = item.Reference.LastKnownAddress;
		if (address is null || !address.Scheme.Equals(WindowsStorageSource.FileAddressScheme, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		return syncRootPaths.Contains(Path.TrimEndingDirectorySeparator(address.Value));
	}
}

internal sealed record NavigationSectionData(int Order, SidebarSectionType SectionType, string Name, StorableReference Reference, IReadOnlyList<NavigationItemData> Items);

internal sealed record NavigationItemData(string Name, StorableReference Reference);

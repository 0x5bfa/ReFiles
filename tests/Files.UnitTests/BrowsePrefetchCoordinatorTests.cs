// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.ItemFeatures.Changes;
using Files.Core.ItemFeatures.Properties;
using Files.Core.ItemFeatures.Thumbnails;
using Files.Core.Models;
using Files.Core.ViewSettings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.UnitTests;

[TestClass]
public sealed class BrowsePrefetchCoordinatorTests
{
	[TestMethod]
	public async Task PrefetchesVisibleAndSurroundingItemsOnly()
	{
		var factory = new TestModelFactory();
		var locationModel = factory.CreateModel("folder", "Folder", out _);
		var order = new List<string>();
		var propertySources = new Dictionary<string, TestPropertySource>();
		var thumbnailSources = new Dictionary<string, TestThumbnailSource>();
		var models = new List<IStorableModel>();
		foreach (var id in new[] { "a", "b", "c", "d" })
		{
			var propertySource = new TestPropertySource
			{
				Handler = (_, _) =>
				{
					order.Add(id);

					return ValueTask.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>());
				},
			};
			var thumbnailSource = new TestThumbnailSource();
			propertySources.Add(id, propertySource);
			thumbnailSources.Add(id, thumbnailSource);
			models.Add(factory.CreateModel(id, id.ToUpperInvariant(), out _, propertySource: propertySource, thumbnailSource: thumbnailSource));
		}

		var resolver = new TestBrowseLocationResolver(models)
		{
			LocationModelFactory = _ => locationModel,
		};
		using var session = new BrowseSessionModel(resolver);
		await session.NavigateAsync(new FolderLocation(locationModel.Reference));

		var settings = new BrowseViewSettings(
			layoutMode: ViewLayoutMode.Details,
			columns: [
				new ViewColumnSettings("System.Size", 120, 0),
				new ViewColumnSettings("System.Hidden", 120, 1, isVisible: false)],
			sortPropertyId: "System.DateModified");
		await using var coordinator = new BrowsePrefetchCoordinator(session);
		coordinator.UpdateViewport(new BrowseViewport(1, 1, 1, dpi: 144), settings, session.Generation);

		await WaitUntilAsync(() => order.Count is 3);

		CollectionAssert.AreEqual(new[] { "b", "c", "a" }, order);
		foreach (var id in new[] { "a", "b", "c" })
		{
			Assert.AreEqual(1, propertySources[id].CallCount);
			CollectionAssert.AreEqual(new[] {"System.Size", "System.DateModified"}, propertySources[id].Requests.Single().ToArray());
			Assert.AreEqual(1, thumbnailSources[id].CallCount);
			Assert.AreEqual(16, thumbnailSources[id].Requests.Single().RequestedSize);
			Assert.AreEqual(24, thumbnailSources[id].Requests.Single().RequestedPixelSize);
			Assert.AreEqual(ThumbnailMode.PreferContent, thumbnailSources[id].Requests.Single().Mode);
		}

		Assert.AreEqual(0, propertySources["d"].CallCount);
		Assert.AreEqual(0, thumbnailSources["d"].CallCount);
	}

	[TestMethod]
	public async Task PublishesResultsAndResortsByPrefetchedProperty()
	{
		var factory = new TestModelFactory();
		var locationModel = factory.CreateModel("folder", "Folder", out _);
		var firstProperties = new TestPropertySource
		{
			Handler = (_, _) => ValueTask.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?> {["System.Size"] = 20L,}),
		};
		var secondProperties = new TestPropertySource
		{
			Handler = (_, _) => ValueTask.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?> {["System.Size"] = 10L,}),
		};
		var firstThumbnail = new TestThumbnailSource
		{
			Handler = (_, _) => ValueTask.FromResult<ThumbnailResult?>(new ThumbnailResult(new byte[] {1}, "image/png", false)),
		};
		var secondThumbnail = new TestThumbnailSource
		{
			Handler = (_, _) => ValueTask.FromResult<ThumbnailResult?>(new ThumbnailResult(new byte[] {2}, "image/png", false)),
		};
		var first = factory.CreateModel("first", "Alpha", out _, propertySource: firstProperties, thumbnailSource: firstThumbnail);
		var second = factory.CreateModel("second", "Beta", out _, propertySource: secondProperties, thumbnailSource: secondThumbnail);
		var resolver = new TestBrowseLocationResolver([first, second])
		{
			LocationModelFactory = _ => locationModel,
		};
		using var session = new BrowseSessionModel(resolver);
		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		var settings = new BrowseViewSettings(columns: [new ViewColumnSettings("System.Size", 120, 0)], sortPropertyId: "System.Size");
		await session.UpdateViewSettingsAsync(settings);
		await using var coordinator = new BrowsePrefetchCoordinator(session);

		coordinator.UpdateViewport(new BrowseViewport(0, 2, 0), settings, session.Generation);

		await WaitUntilAsync(() =>
			session.TryGetPresentation(first.Reference.GetKey(), out var firstPresentation)
			&& firstPresentation.Thumbnail is not null
			&& session.TryGetPresentation(second.Reference.GetKey(), out var secondPresentation)
			&& secondPresentation.Thumbnail is not null);

		Assert.AreSame(second, session.Items[0]);
		Assert.AreSame(first, session.Items[1]);
		Assert.IsTrue(session.TryGetPresentation(first.Reference.GetKey(), out var presentation));
		Assert.AreEqual(20L, presentation.Properties["System.Size"]);
		CollectionAssert.AreEqual(new byte[] {1}, presentation.Thumbnail!.Content.ToArray());
	}

	[TestMethod]
	public async Task ViewportUpdateCancelsPreviousPrefetch()
	{
		var factory = new TestModelFactory();
		var locationModel = factory.CreateModel("folder", "Folder", out _);
		var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var propertySource = new TestPropertySource
		{
			Handler = async (_, cancellationToken) =>
			{
				entered.TrySetResult(true);
				try
				{
					await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
				}
				catch (OperationCanceledException)
				{
					cancelled.TrySetResult(true);
					throw;
				}

				return new Dictionary<string, object?>();
			},
		};
		var item = factory.CreateModel("item", "Item", out _, propertySource: propertySource);
		var resolver = new TestBrowseLocationResolver([item])
		{
			LocationModelFactory = _ => locationModel,
		};
		using var session = new BrowseSessionModel(resolver);
		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		await using var coordinator = new BrowsePrefetchCoordinator(session);
		var settings = new BrowseViewSettings(columns: [new ViewColumnSettings("System.Size", 120, 0)]);

		coordinator.UpdateViewport(new BrowseViewport(0, 1), settings, session.Generation);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		coordinator.UpdateViewport(new BrowseViewport(0, 1), settings, session.Generation);

		await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}

	[TestMethod]
	public async Task NavigationCancelsOldGenerationBeforeUsingItsResult()
	{
		var factory = new TestModelFactory();
		var firstLocation = factory.CreateModel("first", "First", out _);
		var secondLocation = factory.CreateModel("second", "Second", out _);
		var secondItem = factory.CreateModel("second-item", "Second Item", out _);
		var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var firstPropertySource = new TestPropertySource
		{
			Handler = async (_, cancellationToken) =>
			{
				entered.TrySetResult(true);
				try
				{
					await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
				}
				catch (OperationCanceledException)
				{
					completed.TrySetResult(true);

					return new Dictionary<string, object?>();
				}

				return new Dictionary<string, object?>();
			},
		};
		var firstItemWithSource = factory.CreateModel("first-item", "First Item", out _, propertySource: firstPropertySource);
		var locationModels = new Queue<IStorableModel>([firstLocation, secondLocation]);
		var resolver = new TestBrowseLocationResolver([firstItemWithSource])
		{
			LocationModelFactory = _ => locationModels.Dequeue(),
		};
		using var session = new BrowseSessionModel(resolver);
		await session.NavigateAsync(new FolderLocation(firstLocation.Reference));
		await using var coordinator = new BrowsePrefetchCoordinator(session);
		coordinator.UpdateViewport(new BrowseViewport(0, 1), new BrowseViewSettings(columns: [new ViewColumnSettings("System.Size", 120, 0)]), session.Generation);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

		resolver.Items.Clear();
		resolver.Items.Add(secondItem);
		await session.NavigateAsync(new FolderLocation(secondLocation.Reference));
		await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.AreEqual(1, firstPropertySource.CallCount);
		Assert.AreSame(secondItem, session.Items.Single());
	}

	[TestMethod]
	public async Task SameGenerationReplacementCancelsOldSnapshot()
	{
		var factory = new TestModelFactory();
		var changes = new TestFolderChangeSource();
		var locationModel = factory.CreateModel("folder", "Folder", out _, changes);
		var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var oldProperties = new TestPropertySource
		{
			Handler = async (_, cancellationToken) =>
			{
				entered.TrySetResult(true);
				try
				{
					await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
				}
				catch (OperationCanceledException)
				{
					cancelled.TrySetResult(true);
					throw;
				}

				return new Dictionary<string, object?>();
			},
		};
		var oldThumbnail = new TestThumbnailSource();
		var previous = factory.CreateModel("item", "Before", out _, propertySource: oldProperties, thumbnailSource: oldThumbnail);
		var replacement = factory.CreateModel("item", "After", out _);
		var resolver = new TestBrowseLocationResolver([previous])
		{
			LocationModelFactory = _ => locationModel,
			ItemResolver = (_, _) => ValueTask.FromResult<IStorableModel>(replacement),
		};
		using var session = new BrowseSessionModel(resolver);
		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		await using var coordinator = new BrowsePrefetchCoordinator(session);
		var settings = new BrowseViewSettings(columns: [new ViewColumnSettings("System.Size", 120, 0)]);
		var generation = session.Generation;
		coordinator.UpdateViewport(new BrowseViewport(0, 1), settings, generation);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

		changes.RaiseChange(new FolderChange(FolderChangeKind.Updated, replacement.Reference, null, RequiresRefresh: false));

		await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await WaitUntilAsync(() => ReferenceEquals(session.Items.Single(), replacement));
		Assert.AreEqual(generation, session.Generation);
		Assert.AreEqual(0, oldThumbnail.CallCount);
		Assert.IsFalse(session.TryGetPresentation(replacement.Reference.GetKey(), out _));
	}

	[TestMethod]
	public async Task SameGenerationReplacementRejectsResultThatIgnoresCancellation()
	{
		var factory = new TestModelFactory();
		var changes = new TestFolderChangeSource();
		var locationModel = factory.CreateModel("folder", "Folder", out _, changes);
		var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var returned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var oldProperties = new TestPropertySource
		{
			Handler = async (_, _) =>
			{
				entered.TrySetResult(true);
				await release.Task;
				returned.TrySetResult(true);

				return new Dictionary<string, object?>
				{
					["System.Size"] = 42L,
				};
			},
		};
		var previous = factory.CreateModel("item", "Before", out _, propertySource: oldProperties);
		var replacement = factory.CreateModel("item", "After", out _);
		var resolver = new TestBrowseLocationResolver([previous])
		{
			LocationModelFactory = _ => locationModel,
			ItemResolver = (_, _) => ValueTask.FromResult<IStorableModel>(replacement),
		};
		using var session = new BrowseSessionModel(resolver);
		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		await using var coordinator = new BrowsePrefetchCoordinator(session);
		var settings = new BrowseViewSettings(columns: [new ViewColumnSettings("System.Size", 120, 0)]);
		coordinator.UpdateViewport(new BrowseViewport(0, 1), settings, session.Generation);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

		changes.RaiseChange(new FolderChange(FolderChangeKind.Updated, replacement.Reference, null, RequiresRefresh: false));
		await WaitUntilAsync(() => ReferenceEquals(session.Items.Single(), replacement));
		release.TrySetResult(true);
		await returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await coordinator.DisposeAsync();

		Assert.IsFalse(session.TryGetPresentation(replacement.Reference.GetKey(), out _));
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
		while (!condition() && DateTime.UtcNow < timeout)
		{
			await Task.Delay(10).ConfigureAwait(false);
		}

		Assert.IsTrue(condition());
	}
}

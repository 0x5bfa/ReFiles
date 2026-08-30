// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Capabilities.Changes;
using Files.Core.Capabilities.Properties;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Models;
using Files.Core.ViewSettings;
using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for browse prefetch coordinator behavior.
/// </summary>
[TestClass]
public sealed class BrowsePrefetchCoordinatorTests
{
	/// <summary>
	/// Test case: prefetches visible and surrounding items only.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task PrefetchesVisibleAndSurroundingItemsOnly()
	{
		var factory = new TestModelFactory();
		var locationModel = factory.CreateModel("folder", "Folder", out _);
		var order = new ConcurrentQueue<string>();
		var propertySources = new Dictionary<string, TestPropertySource>();
		var thumbnailSources = new Dictionary<string, TestThumbnailSource>();
		var models = new List<IStorableModel>();
		foreach (var id in new[] { "a", "b", "c", "d" })
		{
			var propertySource = new TestPropertySource
			{
				Handler = (_, _) =>
				{
					order.Enqueue(id);

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
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(locationModel.Reference));

		var settings = new BrowseViewSettings(
			layoutMode: ViewLayoutMode.Details,
			columns: [
				new ViewColumnSettings("System.Size", 120, 0),
				new ViewColumnSettings("System.Hidden", 120, 1, isVisible: false)],
			sortPropertyId: "System.DateModified",
			groupPropertyId: "System.ItemTypeText");
		await using var coordinator = new BrowsePrefetchCoordinator(session);
		coordinator.UpdateViewport(new BrowseViewport(1, 1, 1, dpi: 144), settings, session.Generation);

		await WaitUntilAsync(() => order.Count is 3);

		CollectionAssert.AreEquivalent(new[] { "a", "b", "c" }, order.ToArray());
		foreach (var id in new[] { "a", "b", "c" })
		{
			Assert.AreEqual(1, propertySources[id].CallCount);
			CollectionAssert.AreEqual(new[] {"System.Size", "System.DateModified", "System.ItemTypeText"}, propertySources[id].Requests.Single().ToArray());
			Assert.AreEqual(1, thumbnailSources[id].CallCount);
			Assert.AreEqual(16, thumbnailSources[id].Requests.Single().RequestedSize);
			Assert.AreEqual(24, thumbnailSources[id].Requests.Single().RequestedPixelSize);
			Assert.AreEqual(ThumbnailMode.Icon, thumbnailSources[id].Requests.Single().Mode);
		}

		Assert.AreEqual(0, propertySources["d"].CallCount);
		Assert.AreEqual(0, thumbnailSources["d"].CallCount);
	}

	/// <summary>
	/// Test case: grouping change restarts prefetch with the group property.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task GroupingChangeRestartsPrefetchWithTheGroupProperty()
	{
		var factory = new TestModelFactory();
		var locationModel = factory.CreateModel("folder", "Folder", out _);
		var propertySource = new TestPropertySource();
		var item = factory.CreateModel("item", "Item", out _, propertySource: propertySource);
		var resolver = new TestBrowseLocationResolver([item])
		{
			LocationModelFactory = _ => locationModel,
		};
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		await using var coordinator = new BrowsePrefetchCoordinator(session);
		coordinator.UpdateViewport(new BrowseViewport(0, 1), session.ViewSettings, session.Generation);

		await session.UpdateViewSettingsAsync(new BrowseViewSettings(groupPropertyId: "System.ItemTypeText"));

		await WaitUntilAsync(() => propertySource.CallCount is 1);
		CollectionAssert.AreEqual(new[] {"System.ItemTypeText"}, propertySource.Requests.Single().ToArray());
	}

	/// <summary>
	/// Test case: publishes results and resorts by prefetched property.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task PublishesResultsAndResortsByPrefetchedProperty()
	{
		var factory = new TestModelFactory();
		var locationModel = factory.CreateModel("folder", "Folder", out _);
		var firstProperties = new TestPropertySource
		{
			Handler = (_, _) => ValueTask.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?> {["System.Size"] = new FormattedPropertyValue(20L, "1 KB"),}),
		};
		var secondProperties = new TestPropertySource
		{
			Handler = (_, _) => ValueTask.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?> {["System.Size"] = new FormattedPropertyValue(10L, "2 KB"),}),
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
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		var settings = new BrowseViewSettings(columns: [new ViewColumnSettings("System.Size", 120, 0)], sortPropertyId: "System.Size");
		await session.UpdateViewSettingsAsync(settings);
		await using var coordinator = new BrowsePrefetchCoordinator(session);

		coordinator.UpdateViewport(new BrowseViewport(0, 2, 0), settings, session.Generation);

		await WaitUntilAsync(() =>
			session.TryGetPresentation(first.Reference.GetKey(), out var firstPresentation)
			&& firstPresentation.Thumbnail is not null
			&& session.TryGetPresentation(second.Reference.GetKey(), out var secondPresentation)
			&& secondPresentation.Thumbnail is not null
			&& ReferenceEquals(session.Items[0], second));

		Assert.AreSame(second, session.Items[0]);
		Assert.AreSame(first, session.Items[1]);
		Assert.IsTrue(session.TryGetPresentation(first.Reference.GetKey(), out var presentation));
		var size = Assert.IsInstanceOfType<FormattedPropertyValue>(presentation.Properties["System.Size"]);
		Assert.AreEqual(20L, size.RawValue);
		Assert.AreEqual("1 KB", size.DisplayText);
		CollectionAssert.AreEqual(new byte[] {1}, presentation.Thumbnail!.Content.ToArray());
	}

	/// <summary>
	/// Test case: viewport update cancels previous prefetch.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
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
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		await using var coordinator = new BrowsePrefetchCoordinator(session);
		var settings = new BrowseViewSettings(columns: [new ViewColumnSettings("System.Size", 120, 0)]);

		coordinator.UpdateViewport(new BrowseViewport(0, 1), settings, session.Generation);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		coordinator.UpdateViewport(new BrowseViewport(0, 1), settings, session.Generation);

		await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}

	/// <summary>
	/// Test case: item append does not cancel active prefetch.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task ItemAppendDoesNotCancelActivePrefetch()
	{
		var factory = new TestModelFactory();
		var source = new TestFolderChangeSource();
		var locationModel = factory.CreateModel("folder", "Folder", out _, source);
		var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var propertySource = new TestPropertySource
		{
			Handler = async (_, cancellationToken) =>
			{
				entered.TrySetResult(true);
				try
				{
					await release.Task.WaitAsync(cancellationToken);
				}
				catch (OperationCanceledException)
				{
					cancelled.TrySetResult(true);
					throw;
				}

				return new Dictionary<string, object?>();
			},
		};
		var first = factory.CreateModel("first", "First", out _, propertySource: propertySource);
		var appended = factory.CreateModel("appended", "Appended", out _);
		var resolver = new TestBrowseLocationResolver([first])
		{
			LocationModelFactory = _ => locationModel,
			ItemResolver = (_, _) => ValueTask.FromResult<IStorableModel>(appended),
		};
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		await session.UpdateViewSettingsAsync(new BrowseViewSettings(columns: [new ViewColumnSettings("System.Size", 120, 0)]));
		await using var coordinator = new BrowsePrefetchCoordinator(session);
		var settings = session.ViewSettings;

		coordinator.UpdateViewport(new BrowseViewport(0, 1), settings, session.Generation);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		resolver.Items.Add(appended);
		source.RaiseChange(new FolderChange(FolderChangeKind.Created, appended.Reference, null, RequiresRefresh: false));
		await WaitUntilAsync(() => session.Items.Count is 2);
		await Task.Delay(TimeSpan.FromMilliseconds(250));

		Assert.IsFalse(cancelled.Task.IsCompleted);
		release.TrySetResult(true);
	}

	/// <summary>
	/// Test case: navigation cancels old generation before using its result.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
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
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(firstLocation.Reference));
		var settings = new BrowseViewSettings(columns: [new ViewColumnSettings("System.Size", 120, 0)]);
		await session.UpdateViewSettingsAsync(settings);
		await using var coordinator = new BrowsePrefetchCoordinator(session);
		coordinator.UpdateViewport(new BrowseViewport(0, 1), settings, session.Generation);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

		resolver.Items.Clear();
		resolver.Items.Add(secondItem);
		await session.NavigateAsync(new FolderLocation(secondLocation.Reference));
		await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.AreEqual(1, firstPropertySource.CallCount);
		Assert.AreSame(secondItem, session.Items.Single());
	}

	/// <summary>
	/// Test case: same generation replacement cancels old snapshot.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
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
		using var session = new BrowseSession(resolver);
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
		Assert.IsFalse(session.TryGetPresentation(replacement.Reference.GetKey(), out _));
	}

	/// <summary>
	/// Test case: slow properties do not block thumbnail prefetch.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task SlowPropertiesDoNotBlockThumbnailPrefetch()
	{
		var factory = new TestModelFactory();
		var locationModel = factory.CreateModel("folder", "Folder", out _);
		var propertyStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var propertyRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var propertySource = new TestPropertySource
		{
			Handler = async (_, cancellationToken) =>
			{
				propertyStarted.TrySetResult(true);
				await propertyRelease.Task.WaitAsync(cancellationToken);

				return new Dictionary<string, object?>();
			},
		};
		var thumbnailSource = new TestThumbnailSource
		{
			Handler = (_, _) => ValueTask.FromResult<ThumbnailResult?>(new ThumbnailResult(new byte[] { 1 }, "image/png", isFallback: false)),
		};
		var item = factory.CreateModel("item", "Item", out _, propertySource: propertySource, thumbnailSource: thumbnailSource);
		var resolver = new TestBrowseLocationResolver([item])
		{
			LocationModelFactory = _ => locationModel,
		};
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		await using var coordinator = new BrowsePrefetchCoordinator(session);
		var settings = new BrowseViewSettings(columns: [new ViewColumnSettings("System.Size", 120, 0)]);

		coordinator.UpdateViewport(new BrowseViewport(0, 1), settings, session.Generation);
		await propertyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await WaitUntilAsync(() => session.TryGetPresentation(item.Reference.GetKey(), out var presentation) && presentation.Thumbnail is not null);

		Assert.AreEqual(1, thumbnailSource.CallCount);
		propertyRelease.TrySetResult(true);
	}

	/// <summary>
	/// Test case: publishes system icons before loading content thumbnails.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task PublishesSystemIconsBeforeLoadingContentThumbnails()
	{
		var factory = new TestModelFactory();
		var locationModel = factory.CreateModel("folder", "Folder", out _);
		var contentStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var contentRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var thumbnailSource = new TestThumbnailSource
		{
			Handler = async (request, cancellationToken) =>
			{
				if (request.Mode is ThumbnailMode.Icon)
				{
					return new ThumbnailResult(new byte[] { 1 }, "image/png", isFallback: true);
				}

				contentStarted.TrySetResult(true);
				await contentRelease.Task.WaitAsync(cancellationToken);

				return new ThumbnailResult(new byte[] { 2 }, "image/png", isFallback: false);
			},
		};
		var item = factory.CreateModel("item", "Item", out _, thumbnailSource: thumbnailSource);
		var resolver = new TestBrowseLocationResolver([item])
		{
			LocationModelFactory = _ => locationModel,
		};
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		var publishedContent = new ConcurrentQueue<byte>();
		session.ItemPresentationChanged += (_, args) =>
		{
			if ((args.Changed & BrowseItemPresentationChangeFlags.Thumbnail) is not 0 && args.Presentation.Thumbnail is { } thumbnail)
			{
				publishedContent.Enqueue(thumbnail.Content.Span[0]);
			}
		};
		await using var coordinator = new BrowsePrefetchCoordinator(session);
		var settings = new BrowseViewSettings(layoutMode: ViewLayoutMode.Grid);

		try
		{
			coordinator.UpdateViewport(new BrowseViewport(0, 1), settings, session.Generation);
			await contentStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
			CollectionAssert.AreEqual(new byte[] { 1 }, publishedContent.ToArray());
			contentRelease.TrySetResult(true);
			await WaitUntilAsync(() => publishedContent.Count is 2);
		}
		finally
		{
			contentRelease.TrySetResult(true);
		}

		CollectionAssert.AreEqual(new byte[] { 1, 2 }, publishedContent.ToArray());
		CollectionAssert.AreEqual(new[] { ThumbnailMode.Icon, ThumbnailMode.PreferContent }, thumbnailSource.Requests.Select(static request => request.Mode).ToArray());
	}

	/// <summary>
	/// Test case: equivalent thumbnail payloads do not produce repeated presentation changes.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task DoesNotPublishEquivalentThumbnailTwice()
	{
		var factory = new TestModelFactory();
		var locationModel = factory.CreateModel("folder", "Folder", out _);
		var item = factory.CreateModel("item", "Item", out _);
		var resolver = new TestBrowseLocationResolver([item])
		{
			LocationModelFactory = _ => locationModel,
		};
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		var thumbnailChanges = 0;
		session.ItemPresentationChanged += (_, args) =>
		{
			if ((args.Changed & BrowseItemPresentationChangeFlags.Thumbnail) is not 0)
			{
				thumbnailChanges++;
			}
		};
		var target = (IBrowsePrefetchTarget)session;
		var first = await target.PublishThumbnailAsync(session.Generation, item, new ThumbnailResult(new byte[] { 1, 2, 3 }, "image/png", isFallback: true), CancellationToken.None);
		var duplicate = await target.PublishThumbnailAsync(session.Generation, item, new ThumbnailResult(new byte[] { 1, 2, 3 }, "image/png", isFallback: true), CancellationToken.None);
		var replacement = await target.PublishThumbnailAsync(session.Generation, item, new ThumbnailResult(new byte[] { 4, 5, 6 }, "image/png", isFallback: false), CancellationToken.None);

		Assert.IsTrue(first);
		Assert.IsTrue(duplicate);
		Assert.IsTrue(replacement);
		Assert.AreEqual(2, thumbnailChanges);
		Assert.IsTrue(session.TryGetPresentation(item.Reference.GetKey(), out var presentation));
		CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, presentation.Thumbnail!.Content.ToArray());
	}

	/// <summary>
	/// Test case: publishes concurrently loaded thumbnails in sorted display order.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task PublishesThumbnailsInSortedDisplayOrder()
	{
		var factory = new TestModelFactory();
		var locationModel = factory.CreateModel("folder", "Folder", out _);
		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var firstRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var thirdStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var firstThumbnail = new TestThumbnailSource
		{
			Handler = async (_, cancellationToken) =>
			{
				firstStarted.TrySetResult(true);
				await firstRelease.Task.WaitAsync(cancellationToken);

				return new ThumbnailResult(new byte[] { 1 }, "image/png", isFallback: false);
			},
		};
		var secondThumbnail = new TestThumbnailSource
		{
			Handler = (_, _) =>
			{
				secondCompleted.TrySetResult(true);

				return ValueTask.FromResult<ThumbnailResult?>(new ThumbnailResult(new byte[] { 2 }, "image/png", isFallback: false));
			},
		};
		var thirdThumbnail = new TestThumbnailSource
		{
			Handler = (_, _) =>
			{
				thirdStarted.TrySetResult(true);

				return ValueTask.FromResult<ThumbnailResult?>(new ThumbnailResult(new byte[] { 3 }, "image/png", isFallback: false));
			},
		};
		var third = factory.CreateModel("third", "Gamma", out _, thumbnailSource: thirdThumbnail);
		var second = factory.CreateModel("second", "Beta", out _, thumbnailSource: secondThumbnail);
		var first = factory.CreateModel("first", "Alpha", out _, thumbnailSource: firstThumbnail);
		var resolver = new TestBrowseLocationResolver([third, second, first])
		{
			LocationModelFactory = _ => locationModel,
		};
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		CollectionAssert.AreEqual(new[] { first, second, third }, session.Items.ToArray());
		var publishedKeys = new ConcurrentQueue<StorableKey>();
		session.ItemPresentationChanged += (_, args) =>
		{
			if ((args.Changed & BrowseItemPresentationChangeFlags.Thumbnail) is not 0)
			{
				publishedKeys.Enqueue(args.Key);
			}
		};
		await using var coordinator = new BrowsePrefetchCoordinator(session);

		try
		{
			coordinator.UpdateViewport(new BrowseViewport(0, 3), session.ViewSettings, session.Generation);
			await Task.WhenAll(firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)), secondCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5)), thirdStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)));
			Assert.AreEqual(0, publishedKeys.Count);
			firstRelease.TrySetResult(true);
			await WaitUntilAsync(() => publishedKeys.Count is 3);
		}
		finally
		{
			firstRelease.TrySetResult(true);
		}

		CollectionAssert.AreEqual(new[] { first.Reference.GetKey(), second.Reference.GetKey(), third.Reference.GetKey() }, publishedKeys.ToArray());
	}

	/// <summary>
	/// Test case: viewport bursts keep prefetch concurrency bounded.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task ViewportBurstsKeepPrefetchConcurrencyBounded()
	{
		var factory = new TestModelFactory();
		var locationModel = factory.CreateModel("folder", "Folder", out _);
		var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var propertySource = new BoundedPropertySource(release.Task);
		var thumbnailSource = new BoundedThumbnailSource(release.Task);
		var items = Enumerable.Range(0, 4)
			.Select(index => factory.CreateModel($"item-{index}", $"Item {index}", out _, propertySource: propertySource, thumbnailSource: thumbnailSource))
			.Cast<IStorableModel>()
			.ToArray();
		var resolver = new TestBrowseLocationResolver(items)
		{
			LocationModelFactory = _ => locationModel,
		};
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		var coordinator = new BrowsePrefetchCoordinator(session);
		var settings = new BrowseViewSettings(columns: [new ViewColumnSettings("System.Size", 120, 0)]);

		try
		{
			coordinator.UpdateViewport(new BrowseViewport(0, 4), settings, session.Generation);
			await Task.WhenAll(
				propertySource.LaneSaturated.WaitAsync(TimeSpan.FromSeconds(5)),
				thumbnailSource.LaneSaturated.WaitAsync(TimeSpan.FromSeconds(5)));
			for (var updateIndex = 0; updateIndex < 100; updateIndex++)
			{
				coordinator.UpdateViewport(new BrowseViewport(0, 4), settings, session.Generation);
			}

			Assert.AreEqual(2, propertySource.CallCount);
			Assert.AreEqual(2, thumbnailSource.CallCount);
			release.TrySetResult(true);
			await WaitUntilAsync(() => session.TryGetPresentation(items[3].Reference.GetKey(), out var presentation) && presentation.Properties.Count is not 0 && presentation.Thumbnail is not null);
		}
		finally
		{
			release.TrySetResult(true);
			await coordinator.DisposeAsync();
		}

		Assert.IsTrue(propertySource.MaximumConcurrency <= 2);
		Assert.IsTrue(thumbnailSource.MaximumConcurrency <= 2);
		Assert.IsTrue(propertySource.CallCount <= 6);
		Assert.IsTrue(thumbnailSource.CallCount <= 6);
	}

	/// <summary>
	/// Test case: same generation replacement rejects result that ignores cancellation.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
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
		using var session = new BrowseSession(resolver);
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

	private sealed class BoundedPropertySource(Task release) : IPropertySource
	{
		private readonly Task _release = release;
		private readonly TaskCompletionSource<bool> _laneSaturated = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _activeCount;
		private int _callCount;
		private int _maximumConcurrency;

		public int CallCount => Volatile.Read(ref _callCount);

		public int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);

		public Task LaneSaturated => _laneSaturated.Task;

		public async ValueTask<IReadOnlyDictionary<string, object?>> GetPropertiesAsync(PropertyRequest request, CancellationToken cancellationToken = default)
		{
			Interlocked.Increment(ref _callCount);
			var activeCount = Interlocked.Increment(ref _activeCount);
			UpdateMaximum(ref _maximumConcurrency, activeCount);
			if (activeCount is 2)
			{
				_laneSaturated.TrySetResult(true);
			}

			try
			{
				await _release;
			}
			finally
			{
				Interlocked.Decrement(ref _activeCount);
			}

			return new Dictionary<string, object?>
			{
				["System.Size"] = 1L,
			};
		}
	}

	private sealed class BoundedThumbnailSource(Task release) : IThumbnailSource
	{
		private readonly Task _release = release;
		private readonly TaskCompletionSource<bool> _laneSaturated = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _activeCount;
		private int _callCount;
		private int _maximumConcurrency;

		public int CallCount => Volatile.Read(ref _callCount);

		public int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);

		public Task LaneSaturated => _laneSaturated.Task;

		public async ValueTask<ThumbnailResult?> GetThumbnailAsync(ThumbnailRequest request, CancellationToken cancellationToken = default)
		{
			Interlocked.Increment(ref _callCount);
			var activeCount = Interlocked.Increment(ref _activeCount);
			UpdateMaximum(ref _maximumConcurrency, activeCount);
			if (activeCount is 2)
			{
				_laneSaturated.TrySetResult(true);
			}

			try
			{
				await _release;
			}
			finally
			{
				Interlocked.Decrement(ref _activeCount);
			}

			return new ThumbnailResult(new byte[] { 1 }, "image/png", isFallback: false);
		}
	}

	private static void UpdateMaximum(ref int target, int candidate)
	{
		var current = Volatile.Read(ref target);
		while (candidate > current)
		{
			var previous = Interlocked.CompareExchange(ref target, candidate, current);
			if (previous == current)
			{
				return;
			}

			current = previous;
		}
	}
}

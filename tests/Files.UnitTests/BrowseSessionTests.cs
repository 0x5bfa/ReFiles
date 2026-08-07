// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.ItemFeatures.Changes;
using Files.Core.ItemFeatures.Thumbnails;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.ViewSettings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Files.UnitTests;

[TestClass]
public sealed class BrowseSessionTests
{
	[TestMethod]
	public async Task PublishesSortedEnumerationInRanges()
	{
		var factory = new TestModelFactory();
		var locationModel = factory.CreateModel("folder", "Folder", out _);
		var items = Enumerable.Range(0, 600)
			.Select(index => factory.CreateModel($"item-{index:D3}", $"Item {index:D3}", out _))
			.Cast<IStorableModel>()
			.ToArray();
		var resolver = new TestBrowseLocationResolver(items)
		{
			LocationModelFactory = _ => locationModel,
		};
		using var session = new BrowseSession(resolver);
		var publishedBatchSizes = new List<int>();
		session.ItemsChanged += (_, _) =>
		{
			publishedBatchSizes.Add(session.Items.Count - publishedBatchSizes.Sum());
		};

		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		Assert.AreEqual(600, session.Items.Count);
		CollectionAssert.AreEqual(new[] { 32, 256, 312 }, publishedBatchSizes);
	}

	[TestMethod]
	public async Task PublishesFirstBatchBeforeEnumerationCompletes()
	{
		var factory = new TestModelFactory();
		var locationModel = factory.CreateModel("folder", "Folder", out _);
		var items = Enumerable.Range(0, 600)
			.Select(index => factory.CreateModel($"item-{index:D3}", $"Item {index:D3}", out _))
			.Cast<IStorableModel>()
			.ToArray();
		var providerPaused = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var providerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var resolver = new TestBrowseLocationResolver(items)
		{
			LocationModelFactory = _ => locationModel,
			BeforeYieldAsync = async (index, cancellationToken) =>
			{
				if (index is 32)
				{
					providerPaused.TrySetResult(true);
					await providerRelease.Task.WaitAsync(cancellationToken);
				}
			},
		};
		using var session = new BrowseSession(resolver);
		var navigation = session.NavigateAsync(new FolderLocation(locationModel.Reference)).AsTask();

		await providerPaused.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.IsTrue(session.IsLoading);
		Assert.AreEqual(32, session.Items.Count);
		Assert.IsFalse(navigation.IsCompleted);
		providerRelease.TrySetResult(true);
		await navigation;
		Assert.AreEqual(600, session.Items.Count);
	}

	[TestMethod]
	public async Task AppliesRequestedSortAfterProgressiveEnumeration()
	{
		var factory = new TestModelFactory();
		var locationModel = factory.CreateModel("folder", "Folder", out _);
		var items = Enumerable.Range(0, 600)
			.Reverse()
			.Select(index => factory.CreateModel($"item-{index:D3}", $"Item {index:D3}", out _))
			.Cast<IStorableModel>()
			.ToArray();
		var providerPaused = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var providerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var resolver = new TestBrowseLocationResolver(items)
		{
			LocationModelFactory = _ => locationModel,
			BeforeYieldAsync = async (index, cancellationToken) =>
			{
				if (index is 32)
				{
					providerPaused.TrySetResult(true);
					await providerRelease.Task.WaitAsync(cancellationToken);
				}
			},
		};
		using var session = new BrowseSession(resolver);
		var resetCount = 0;
		session.ItemsChanged += (_, args) => resetCount += args.Changes.Count(static change => change is BrowseItemsReset);
		var navigation = session.NavigateAsync(new FolderLocation(locationModel.Reference)).AsTask();

		await providerPaused.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.AreEqual("Item 599", session.Items[0].Name);
		Assert.AreEqual("Item 568", session.Items[^1].Name);
		providerRelease.TrySetResult(true);
		await navigation;

		Assert.AreEqual("Item 000", session.Items[0].Name);
		Assert.AreEqual("Item 599", session.Items[^1].Name);
		Assert.AreEqual(2, resetCount);
	}

	[TestMethod]
	[DataRow(100, 2)]
	[DataRow(1_000, 4)]
	[DataRow(10_000, 12)]
	[DataRow(44_000, 46)]
	public async Task LargeEnumerationsUseBoundedAdaptiveBatches(int itemCount, int expectedNotificationCount)
	{
		var factory = new TestModelFactory();
		var locationModel = factory.CreateModel("folder", "Folder", out _);
		var items = Enumerable.Range(0, itemCount)
			.Select(index => factory.CreateModel($"item-{index:D5}", $"Item {index:D5}", out _))
			.Cast<IStorableModel>()
			.ToArray();
		var resolver = new TestBrowseLocationResolver(items)
		{
			LocationModelFactory = _ => locationModel,
		};
		using var session = new BrowseSession(resolver);
		var notificationCount = 0;
		var firstPublishedCount = 0;
		session.ItemsChanged += (_, _) =>
		{
			Interlocked.CompareExchange(ref firstPublishedCount, session.Items.Count, 0);
			Interlocked.Increment(ref notificationCount);
		};

		await session.NavigateAsync(new FolderLocation(locationModel.Reference));

		Assert.AreEqual(itemCount, session.Items.Count);
		Assert.AreEqual(32, firstPublishedCount);
		Assert.AreEqual(expectedNotificationCount, notificationCount);
	}

	[TestMethod]
	public async Task SortsEnumerationBeforePublishingBatches()
	{
		var factory = new TestModelFactory();
		var locationModel = factory.CreateModel("folder", "Folder", out _);
		var first = factory.CreateModel("first", "Zulu", out _);
		var second = factory.CreateModel("second", "Alpha", out _);
		var resolver = new TestBrowseLocationResolver([first, second])
		{
			LocationModelFactory = _ => locationModel,
		};
		using var session = new BrowseSession(resolver);
		var itemChanges = new List<BrowseItemsChangedEventArgs>();
		session.ItemsChanged += (_, args) => itemChanges.Add(args);

		await session.NavigateAsync(new FolderLocation(locationModel.Reference));

		Assert.AreEqual(1, itemChanges.Count);
		var reset = itemChanges[0].Changes.Single() as BrowseItemsReset;
		Assert.IsNotNull(reset);
		Assert.AreSame(second, reset.Items[0]);
		Assert.AreSame(first, reset.Items[1]);
		Assert.AreSame(second, session.Items[0]);
	}

	[TestMethod]
	public async Task NewNavigationCancelsCurrentEnumeration()
	{
		var factory = new TestModelFactory();
		var firstLocation = factory.CreateModel("first", "First", out _);
		var secondLocation = factory.CreateModel("second", "Second", out _);
		var firstItem = factory.CreateModel("first-item", "First Item", out _);
		var secondItem = factory.CreateModel("second-item", "Second Item", out _);
		var enumerationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var resolver = new TestBrowseLocationResolver([firstItem])
		{
			LocationModelFactory = location => location is FolderLocation folder && folder.Folder == firstLocation.Reference ? firstLocation : secondLocation,
			EnumerationStarted = enumerationStarted,
			BlockEnumeration = true,
		};
		using var session = new BrowseSession(resolver);

		var firstNavigation = session.NavigateAsync(new FolderLocation(firstLocation.Reference)).AsTask();
		await enumerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		resolver.BlockEnumeration = false;
		resolver.Items.Clear();
		resolver.Items.Add(secondItem);

		await session.NavigateAsync(new FolderLocation(secondLocation.Reference));

		await Assert.ThrowsAsync<OperationCanceledException>(async () => await firstNavigation);
		Assert.AreEqual(secondLocation.Reference, ((FolderLocation)session.Location!).Folder);
		Assert.AreSame(secondItem, session.Items.Single());
	}

	[TestMethod]
	public async Task NavigationDisposesPreviousItemsAfterSuccessfulReplacement()
	{
		var factory = new TestModelFactory();
		var first = factory.CreateModel("first", "First", out var firstCore);
		var second = factory.CreateModel("second", "Second", out var secondCore);
		var resolver = new TestBrowseLocationResolver([first]);
		using var session = new BrowseSession(resolver);

		await session.NavigateAsync(new FolderLocation(first.Reference));
		Assert.AreSame(first, session.Items.Single());
		var firstContext = resolver.OpenedContexts.Single();
		Assert.AreSame(firstContext, session.Context);

		resolver.Items.Clear();
		resolver.Items.Add(second);
		await session.NavigateAsync(new FolderLocation(second.Reference));
		Assert.IsTrue(firstCore.IsDisposed);
		Assert.IsFalse(secondCore.IsDisposed);
		Assert.IsTrue(firstContext.IsDisposed);
		Assert.IsFalse(resolver.OpenedContexts.Last().IsDisposed);
	}

	[TestMethod]
	public async Task FailedNavigationKeepsCurrentItemsAndDisposesPartialResults()
	{
		var factory = new TestModelFactory();
		var current = factory.CreateModel("current", "Current", out var currentCore);
		var partial = factory.CreateModel("partial", "Partial", out var partialCore);
		var resolver = new TestBrowseLocationResolver([current]);
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(current.Reference));

		resolver.Items.Clear();
		resolver.Items.Add(partial);
		resolver.Exception = new InvalidOperationException("failure");
		await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.NavigateAsync(new FolderLocation(partial.Reference)));

		Assert.IsFalse(currentCore.IsDisposed);
		Assert.IsTrue(partialCore.IsDisposed);
		Assert.AreSame(resolver.OpenedContexts[0], session.Context);
		Assert.IsTrue(resolver.OpenedContexts[1].IsDisposed);
		Assert.IsNotNull(session.Error);
	}

	[TestMethod]
	public async Task FailureAfterPublishedBatchRollsBackToPreviousItems()
	{
		var factory = new TestModelFactory();
		var firstLocation = factory.CreateModel("first-folder", "First Folder", out _);
		var secondLocation = factory.CreateModel("second-folder", "Second Folder", out _);
		var current = factory.CreateModel("current", "Current", out var currentCore);
		var locationModels = new Queue<IStorableModel>([firstLocation, secondLocation]);
		var resolver = new TestBrowseLocationResolver([current])
		{
			LocationModelFactory = _ => locationModels.Dequeue(),
		};
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(firstLocation.Reference));
		var partialModels = new List<IStorableModel>();
		var partialCores = new List<DisposableStorable>();
		for (var index = 0; index < 40; index++)
		{
			partialModels.Add(factory.CreateModel($"partial-{index}", $"Partial {index}", out var partialCore));
			partialCores.Add(partialCore);
		}
		resolver.Items.Clear();
		foreach (var partialModel in partialModels)
		{
			resolver.Items.Add(partialModel);
		}

		resolver.Exception = new InvalidOperationException("failure after batch");

		await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.NavigateAsync(new FolderLocation(secondLocation.Reference)));

		Assert.AreSame(current, session.Items.Single());
		Assert.IsFalse(currentCore.IsDisposed);
		Assert.IsTrue(partialCores.All(static core => core.IsDisposed));
		Assert.IsNotNull(session.Error);
	}

	[TestMethod]
	public async Task CancelledNavigationDisposesNewContextAndPreservesCurrentState()
	{
		var factory = new TestModelFactory();
		var current = factory.CreateModel("current", "Current", out var currentCore);
		var next = factory.CreateModel("next", "Next", out var nextCore);
		var enumerationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var resolver = new TestBrowseLocationResolver([current]);
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(current.Reference));

		resolver.Items.Clear();
		resolver.Items.Add(next);
		resolver.EnumerationStarted = enumerationStarted;
		resolver.BlockEnumeration = true;
		using var cancellation = new CancellationTokenSource();
		var navigation = session.NavigateAsync(new FolderLocation(next.Reference), cancellation.Token);
		await enumerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		cancellation.Cancel();

		await Assert.ThrowsAsync<OperationCanceledException>(async () => await navigation);

		Assert.IsFalse(currentCore.IsDisposed);
		Assert.IsFalse(nextCore.IsDisposed);
		Assert.AreSame(current, session.Items.Single());
		Assert.AreSame(resolver.OpenedContexts[0], session.Context);
		Assert.IsTrue(resolver.OpenedContexts[1].IsDisposed);
	}

	[TestMethod]
	public async Task CancellationAfterPublishedBatchRestoresPreviousItems()
	{
		var factory = new TestModelFactory();
		var firstLocation = factory.CreateModel("first-folder", "First Folder", out _);
		var secondLocation = factory.CreateModel("second-folder", "Second Folder", out _);
		var current = factory.CreateModel("current", "Current", out var currentCore);
		var locationModels = new Queue<IStorableModel>([firstLocation, secondLocation]);
		var providerPaused = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var providerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var resolver = new TestBrowseLocationResolver([current])
		{
			LocationModelFactory = _ => locationModels.Dequeue(),
		};
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(firstLocation.Reference));
		var nextModels = new List<IStorableModel>();
		var nextCores = new List<DisposableStorable>();
		for (var index = 0; index < 40; index++)
		{
			nextModels.Add(factory.CreateModel($"next-{index}", $"Next {index}", out var nextCore));
			nextCores.Add(nextCore);
		}
		resolver.Items.Clear();
		foreach (var nextModel in nextModels)
		{
			resolver.Items.Add(nextModel);
		}

		resolver.BeforeYieldAsync = async (index, cancellationToken) =>
		{
			if (index is 32)
			{
				providerPaused.TrySetResult(true);
				await providerRelease.Task.WaitAsync(cancellationToken);
			}
		};
		using var cancellation = new CancellationTokenSource();
		var navigation = session.NavigateAsync(new FolderLocation(secondLocation.Reference), cancellation.Token).AsTask();
		await providerPaused.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.AreEqual(32, session.Items.Count);

		cancellation.Cancel();
		await Assert.ThrowsAsync<OperationCanceledException>(async () => await navigation);

		Assert.AreSame(current, session.Items.Single());
		Assert.IsFalse(currentCore.IsDisposed);
		Assert.IsTrue(nextCores.Take(32).All(static core => core.IsDisposed));
		Assert.IsTrue(nextCores.Skip(32).All(static core => !core.IsDisposed));
		Assert.IsTrue(resolver.OpenedContexts[1].IsDisposed);
		foreach (var unyieldedModel in nextModels.Skip(32))
		{
			await unyieldedModel.DisposeAsync();
		}
	}

	[TestMethod]
	public async Task DisposingSessionDisposesActiveContextAndItems()
	{
		var factory = new TestModelFactory();
		var item = factory.CreateModel("item", "Item", out var itemCore);
		var resolver = new TestBrowseLocationResolver([item]);
		var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(item.Reference));
		var context = resolver.OpenedContexts.Single();

		await session.DisposeAsync();

		Assert.IsTrue(itemCore.IsDisposed);
		Assert.IsTrue(context.IsDisposed);
		Assert.IsEmpty(session.Items);
		Assert.IsNull(session.Context);
	}

	[TestMethod]
	public async Task StartsWatcherBeforeInitialEnumeration()
	{
		var factory = new TestModelFactory();
		var changeSource = new TestFolderChangeSource();
		var locationModel = factory.CreateModel("folder", "Folder", out _, changeSource);
		var resolver = new TestBrowseLocationResolver([])
		{
			LocationModelFactory = _ => locationModel,
			EnumerationGuard = () => changeSource.IsStarted,
		};
		using var session = new BrowseSession(resolver);

		await session.NavigateAsync(new FolderLocation(locationModel.Reference));

		Assert.AreEqual(1, changeSource.StartCount);
	}

	[TestMethod]
	public async Task NotificationDuringEnumerationTriggersRefreshAfterActivation()
	{
		var factory = new TestModelFactory();
		var firstSource = new TestFolderChangeSource();
		var secondSource = new TestFolderChangeSource();
		var firstModel = factory.CreateModel("first", "First", out _, firstSource);
		var secondModel = factory.CreateModel("second", "Second", out _, secondSource);
		var locationModels = new Queue<IStorableModel>([firstModel, secondModel]);
		var refreshed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var resolver = new TestBrowseLocationResolver([])
		{
			LocationModelFactory = _ => locationModels.Dequeue(),
		};
		using var session = new BrowseSession(resolver);
		session.StateChanged += (_, _) =>
		{
			if (resolver.OpenedContexts.Count is 2 && !session.IsLoading && ReferenceEquals(session.Context, resolver.OpenedContexts[1]))
			{
				refreshed.TrySetResult(true);
			}
		};
		resolver.EnumerationAction = firstSource.RaiseChange;

		await session.NavigateAsync(new FolderLocation(firstModel.Reference));
		await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.AreEqual(2, resolver.OpenedContexts.Count);
		Assert.IsTrue(resolver.OpenedContexts[0].IsDisposed);
		Assert.AreEqual(1, secondSource.StartCount);
	}

	[TestMethod]
	public async Task DetailedChangeDuringNextEnumerationIsAppliedAfterActivation()
	{
		var factory = new TestModelFactory();
		var firstSource = new TestFolderChangeSource();
		var secondSource = new TestFolderChangeSource();
		var firstLocation = factory.CreateModel("first", "First", out _, firstSource);
		var secondLocation = factory.CreateModel("second", "Second", out _, secondSource);
		var created = factory.CreateModel("created", "Created", out _);
		var locationModels = new Queue<IStorableModel>([firstLocation, secondLocation]);
		var resolver = new TestBrowseLocationResolver([])
		{
			LocationModelFactory = _ => locationModels.Dequeue(),
			ItemResolver = (_, _) => ValueTask.FromResult<IStorableModel>(created),
		};
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(firstLocation.Reference));

		resolver.EnumerationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		resolver.EnumerationRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		resolver.BlockEnumeration = true;
		var navigation = session
			.NavigateAsync(new FolderLocation(secondLocation.Reference))
			.AsTask();
		await resolver.EnumerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		secondSource.RaiseChange(new FolderChange(FolderChangeKind.Created, created.Reference, null, RequiresRefresh: false));

		resolver.EnumerationRelease.TrySetResult(true);
		await navigation.WaitAsync(TimeSpan.FromSeconds(5));
		await WaitUntilAsync(() => session.Items.Count is 1 && ReferenceEquals(session.Items[0], created));

		Assert.AreEqual(2, resolver.OpenedContexts.Count);
		Assert.AreSame(resolver.OpenedContexts[1], session.Context);
	}

	[TestMethod]
	public async Task NotificationBurstIsCoalescedIntoOneRefresh()
	{
		var factory = new TestModelFactory();
		var firstSource = new TestFolderChangeSource();
		var secondSource = new TestFolderChangeSource();
		var firstModel = factory.CreateModel("first", "First", out _, firstSource);
		var secondModel = factory.CreateModel("second", "Second", out _, secondSource);
		var locationModels = new Queue<IStorableModel>([firstModel, secondModel]);
		var refreshed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var resolver = new TestBrowseLocationResolver([])
		{
			LocationModelFactory = _ => locationModels.Dequeue(),
		};
		using var session = new BrowseSession(resolver);
		session.StateChanged += (_, _) =>
		{
			if (resolver.OpenedContexts.Count is 2 && !session.IsLoading && ReferenceEquals(session.Context, resolver.OpenedContexts[1]))
			{
				refreshed.TrySetResult(true);
			}
		};

		await session.NavigateAsync(new FolderLocation(firstModel.Reference));
		for (var index = 0; index < 100; index++)
		{
			firstSource.RaiseChange();
		}

		await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Task.Delay(100);

		Assert.AreEqual(2, resolver.OpenedContexts.Count);
		Assert.AreEqual(1, secondSource.StartCount);
	}

	[TestMethod]
	public async Task NotificationsFromPreviousContextAreIgnoredAfterNavigation()
	{
		var factory = new TestModelFactory();
		var firstSource = new TestFolderChangeSource();
		var secondSource = new TestFolderChangeSource();
		var firstModel = factory.CreateModel("first", "First", out _, firstSource);
		var secondModel = factory.CreateModel("second", "Second", out _, secondSource);
		var locationModels = new Queue<IStorableModel>([firstModel, secondModel]);
		var resolver = new TestBrowseLocationResolver([])
		{
			LocationModelFactory = _ => locationModels.Dequeue(),
		};
		using var session = new BrowseSession(resolver);

		await session.NavigateAsync(new FolderLocation(firstModel.Reference));
		await session.NavigateAsync(new FolderLocation(secondModel.Reference));
		firstSource.RaiseChange();
		await Task.Delay(100);

		Assert.AreEqual(2, resolver.OpenedContexts.Count);
		Assert.IsTrue(firstSource.IsDisposed);
		Assert.IsFalse(secondSource.IsDisposed);
	}

	[TestMethod]
	public async Task FailedRefreshPreservesCurrentItemsAndContext()
	{
		var factory = new TestModelFactory();
		var currentItem = factory.CreateModel("item", "Item", out var currentCore);
		var partialItem = factory.CreateModel("partial", "Partial", out var partialCore);
		var firstSource = new TestFolderChangeSource();
		var secondSource = new TestFolderChangeSource();
		var firstModel = factory.CreateModel("first", "First", out _, firstSource);
		var secondModel = factory.CreateModel("second", "Second", out _, secondSource);
		var locationModels = new Queue<IStorableModel>([firstModel, secondModel]);
		var errorObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var resolver = new TestBrowseLocationResolver([currentItem])
		{
			LocationModelFactory = _ => locationModels.Dequeue(),
		};
		using var session = new BrowseSession(resolver);
		session.StateChanged += (_, _) =>
		{
			if (session.Error is not null && !session.IsLoading)
			{
				errorObserved.TrySetResult(true);
			}
		};

		await session.NavigateAsync(new FolderLocation(firstModel.Reference));
		resolver.Items.Clear();
		resolver.Items.Add(partialItem);
		resolver.Exception = new InvalidOperationException("refresh failed");
		firstSource.RaiseChange();

		await errorObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.AreSame(currentItem, session.Items.Single());
		Assert.IsFalse(currentCore.IsDisposed);
		Assert.IsTrue(partialCore.IsDisposed);
		Assert.AreSame(resolver.OpenedContexts[0], session.Context);
		Assert.IsTrue(resolver.OpenedContexts[1].IsDisposed);
		Assert.IsNotNull(session.Error);
	}

	[TestMethod]
	public async Task CreatedChangeAddsOneItem()
	{
		var factory = new TestModelFactory();
		var source = new TestFolderChangeSource();
		var locationModel = factory.CreateModel("folder", "Folder", out _, source);
		var created = factory.CreateModel("created", "Created", out _);
		var resolver = CreateIncrementalResolver(locationModel, created, created.Reference);
		using var session = new BrowseSession(resolver);

		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		source.RaiseChange(new FolderChange(FolderChangeKind.Created, created.Reference, null, RequiresRefresh: false));

		await WaitUntilAsync(() => session.Items.Count is 1);

		Assert.AreSame(created, session.Items.Single());
	}

	[TestMethod]
	public async Task DuplicateCreatedChangeDoesNotDuplicateItem()
	{
		var factory = new TestModelFactory();
		var source = new TestFolderChangeSource();
		var locationModel = factory.CreateModel("folder", "Folder", out _, source);
		var created = factory.CreateModel("created", "Created", out _);
		var resolveCount = 0;
		var resolver = CreateIncrementalResolver(locationModel, created, created.Reference, itemResolver: (_, _) => { resolveCount++; return ValueTask.FromResult<IStorableModel>(created); });
		using var session = new BrowseSession(resolver);

		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		var change = new FolderChange(FolderChangeKind.Created, created.Reference, null, RequiresRefresh: false);
		source.RaiseChange(change);
		await WaitUntilAsync(() => session.Items.Count is 1);
		source.RaiseChange(change);
		await Task.Delay(100);

		Assert.AreEqual(1, session.Items.Count);
		Assert.AreSame(created, session.Items.Single());
		Assert.AreEqual(1, resolveCount);
	}

	[TestMethod]
	public async Task DeletedChangeRemovesAndDisposesItemOnce()
	{
		var factory = new TestModelFactory();
		var source = new TestFolderChangeSource();
		var locationModel = factory.CreateModel("folder", "Folder", out _, source);
		var deleted = factory.CreateModel("deleted", "Deleted", out var deletedCore);
		var resolver = CreateIncrementalResolver(locationModel, deleted, deleted.Reference, items: [deleted]);
		using var session = new BrowseSession(resolver);

		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		source.RaiseChange(new FolderChange(FolderChangeKind.Deleted, null, deleted.Reference, RequiresRefresh: false));

		await WaitUntilAsync(() => session.Items.Count is 0 && deletedCore.DisposeCount is 1);

		Assert.AreEqual(1, deletedCore.DisposeCount);
	}

	[TestMethod]
	public async Task RenamedChangeReplacesModelInstance()
	{
		var factory = new TestModelFactory();
		var source = new TestFolderChangeSource();
		var locationModel = factory.CreateModel("folder", "Folder", out _, source);
		var previous = factory.CreateModel("item", "Before", out var previousCore);
		var replacement = factory.CreateModel("item", "After", out _);
		var currentReference = new StorableReference(previous.Reference.SourceId, previous.Reference.ItemId, new StorageAddress("test", "renamed"));
		var resolver = CreateIncrementalResolver(locationModel, replacement, currentReference, items: [previous]);
		using var session = new BrowseSession(resolver);

		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		source.RaiseChange(new FolderChange(FolderChangeKind.Renamed, currentReference, previous.Reference, RequiresRefresh: false));

		await WaitUntilAsync(() => ReferenceEquals(session.Items.Single(), replacement));

		Assert.AreNotSame(previous, replacement);
		Assert.AreEqual(1, previousCore.DisposeCount);
	}

	[TestMethod]
	public async Task UpdatedChangeReplacesModelAndInvalidatesCache()
	{
		var factory = new TestModelFactory();
		var source = new TestFolderChangeSource();
		var locationModel = factory.CreateModel("folder", "Folder", out _, source);
		var previous = factory.CreateModel("item", "Before", out var previousCore);
		var replacement = factory.CreateModel("item", "After", out _);
		var cache = new TestThumbnailCache();
		var resolver = CreateIncrementalResolver(locationModel, replacement, previous.Reference, items: [previous]);
		using var session = new BrowseSession(resolver, thumbnailCache: cache);

		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		source.RaiseChange(new FolderChange(FolderChangeKind.Updated, replacement.Reference, null, RequiresRefresh: false));

		await WaitUntilAsync(() => ReferenceEquals(session.Items.Single(), replacement));

		Assert.AreEqual(1, previousCore.DisposeCount);
		CollectionAssert.Contains(cache.InvalidatedReferences.ToList(), replacement.Reference);
	}

	[TestMethod]
	public async Task CreatedRenamedDeletedChangesPreserveQueueOrder()
	{
		var factory = new TestModelFactory();
		var source = new TestFolderChangeSource();
		var locationModel = factory.CreateModel("folder", "Folder", out _, source);
		var created = factory.CreateModel("sequence", "Created", out var createdCore);
		var renamed = factory.CreateModel("sequence", "Renamed", out var renamedCore);
		var resolver = CreateIncrementalResolver(
			locationModel,
			renamed,
			renamed.Reference,
			items: [],
			itemResolver: (reference, _) =>
			{
				if (reference.ItemId == created.Reference.ItemId)
				{
					return ValueTask.FromResult<IStorableModel>(ReferenceEquals(reference, created.Reference) ? created : renamed);
				}

				return ValueTask.FromResult<IStorableModel>(renamed);
			});
		using var session = new BrowseSession(resolver);

		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		var renamedReference = new StorableReference(created.Reference.SourceId, created.Reference.ItemId, new StorageAddress("test", "renamed"));
		source.RaiseChange(new FolderChange(FolderChangeKind.Created, created.Reference, null, RequiresRefresh: false));
		source.RaiseChange(new FolderChange(FolderChangeKind.Renamed, renamedReference, created.Reference, RequiresRefresh: false));
		source.RaiseChange(new FolderChange(FolderChangeKind.Deleted, null, renamedReference, RequiresRefresh: false));

		await WaitUntilAsync(() => session.Items.Count is 0 && createdCore.DisposeCount is 1 && renamedCore.DisposeCount is 1);

		Assert.AreEqual(0, session.Items.Count);
		Assert.IsTrue(createdCore.IsDisposed);
		Assert.IsTrue(renamedCore.IsDisposed);
	}

	[TestMethod]
	public async Task IncompleteChangeFallsBackToFullRefresh()
	{
		var factory = new TestModelFactory();
		var firstSource = new TestFolderChangeSource();
		var secondSource = new TestFolderChangeSource();
		var firstLocation = factory.CreateModel("first", "First", out _, firstSource);
		var secondLocation = factory.CreateModel("second", "Second", out _, secondSource);
		var oldItem = factory.CreateModel("old", "Old", out var oldItemCore);
		var newItem = factory.CreateModel("new", "New", out _);
		var locationModels = new Queue<IStorableModel>([firstLocation, secondLocation]);
		var resolver = new TestBrowseLocationResolver([oldItem])
		{
			LocationModelFactory = _ => locationModels.Dequeue(),
		};
		using var session = new BrowseSession(resolver);

		await session.NavigateAsync(new FolderLocation(firstLocation.Reference));
		resolver.Items.Clear();
		resolver.Items.Add(newItem);
		firstSource.RaiseChange(new FolderChange(FolderChangeKind.DirectoryUpdated, null, null, RequiresRefresh: false));

		await WaitUntilAsync(() => ReferenceEquals(session.Items.Single(), newItem) && oldItemCore.IsDisposed);

		Assert.AreEqual(2, resolver.OpenedContexts.Count);
		Assert.IsTrue(oldItemCore.IsDisposed);
	}

	[TestMethod]
	public async Task StaleResolveResultIsDisposedAfterNavigation()
	{
		var factory = new TestModelFactory();
		var firstSource = new TestFolderChangeSource();
		var secondSource = new TestFolderChangeSource();
		var firstLocation = factory.CreateModel("first", "First", out _, firstSource);
		var secondLocation = factory.CreateModel("second", "Second", out _, secondSource);
		var created = factory.CreateModel("created", "Created", out var createdCore);
		var resolveStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseResolve = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var resolver = CreateIncrementalResolver(
			firstLocation,
			created,
			created.Reference,
			itemResolver: async (reference, cancellationToken) =>
			{
				resolveStarted.TrySetResult(true);
				await releaseResolve.Task.WaitAsync(cancellationToken);

				return created;
			});
		resolver.LocationModelFactory = location =>
			location is FolderLocation folderLocation
				&& folderLocation.Folder == firstLocation.Reference
				? firstLocation
				: secondLocation;
		using var session = new BrowseSession(resolver);

		await session.NavigateAsync(new FolderLocation(firstLocation.Reference));
		firstSource.RaiseChange(new FolderChange(FolderChangeKind.Created, created.Reference, null, RequiresRefresh: false));
		await resolveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

		await session.NavigateAsync(new FolderLocation(secondLocation.Reference));
		releaseResolve.TrySetResult(true);
		await WaitUntilAsync(() => createdCore.DisposeCount is 1);

		Assert.AreSame(secondLocation.Reference, session.Location is FolderLocation location ? location.Folder : null);
		Assert.IsTrue(createdCore.IsDisposed);
	}

	[TestMethod]
	public async Task IncrementalApplyFailureKeepsStateUntilFullRefresh()
	{
		var factory = new TestModelFactory();
		var source = new TestFolderChangeSource();
		var locationModel = factory.CreateModel("folder", "Folder", out _, source);
		var failedLocationModel = factory.CreateModel("folder-refresh", "Folder", out _);
		var current = factory.CreateModel("current", "Current", out var currentCore);
		var partial = factory.CreateModel("partial", "Partial", out var partialCore);
		var locationModels = new Queue<IStorableModel>([ locationModel, failedLocationModel]);
		var resolver = CreateIncrementalResolver(locationModel, current, current.Reference, items: [current], itemResolver: (_, _) => throw new InvalidOperationException("resolve failed"));
		resolver.LocationModelFactory = _ => locationModels.Dequeue();
		using var session = new BrowseSession(resolver);

		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		resolver.Items.Clear();
		resolver.Items.Add(partial);
		resolver.Exception = new InvalidOperationException("refresh failed");
		source.RaiseChange(new FolderChange(FolderChangeKind.Updated, current.Reference, null, RequiresRefresh: false));

		await WaitUntilAsync(() => session.Error is not null);

		Assert.AreSame(current, session.Items.Single());
		Assert.IsFalse(currentCore.IsDisposed);
		Assert.IsTrue(partialCore.IsDisposed);
	}

	[TestMethod]
	public async Task RenamedItemReplacesModelAndPreservesSelectionKey()
	{
		var factory = new TestModelFactory();
		var source = new TestFolderChangeSource();
		var locationModel = factory.CreateModel("folder", "Folder", out _, source);
		var previous = factory.CreateModel("item", "Before", out _);
		var replacement = factory.CreateModel("item", "After", out _);
		var currentReference = new StorableReference(previous.Reference.SourceId, previous.Reference.ItemId, new StorageAddress("test", "renamed"));
		var resolver = CreateIncrementalResolver(locationModel, replacement, currentReference, items: [previous]);
		using var session = new BrowseSession(resolver);
		var itemChanges = new List<BrowseItemsChangedEventArgs>();
		session.ItemsChanged += (_, args) => itemChanges.Add(args);

		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		var key = previous.Reference.GetKey();
		session.SetSelection([key, key], key, key);
		source.RaiseChange(new FolderChange(FolderChangeKind.Renamed, currentReference, previous.Reference, RequiresRefresh: false));

		await WaitUntilAsync(() => ReferenceEquals(session.Items.Single(), replacement));

		Assert.AreEqual(1, session.Selection.SelectedKeys.Count);
		Assert.AreEqual(key, session.Selection.SelectedKeys[0]);
		Assert.AreEqual(key, session.Selection.FocusedKey);
		Assert.AreEqual(key, session.Selection.AnchorKey);
		Assert.AreEqual(2, itemChanges[^1].Version);
		Assert.IsInstanceOfType<BrowseItemReplaced>(itemChanges[^1].Changes[0]);
	}

	[TestMethod]
	public async Task RenamedItemMigratesSelectionWhenIdentityChanges()
	{
		var factory = new TestModelFactory();
		var source = new TestFolderChangeSource();
		var locationModel = factory.CreateModel("folder", "Folder", out _, source);
		var previous = factory.CreateModel("old", "Before", out _);
		var replacement = factory.CreateModel("new", "After", out _);
		var resolver = CreateIncrementalResolver(locationModel, replacement, replacement.Reference, items: [previous]);
		using var session = new BrowseSession(resolver);

		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		var previousKey = previous.Reference.GetKey();
		var replacementKey = replacement.Reference.GetKey();
		session.SetSelection([previousKey], previousKey, previousKey);
		source.RaiseChange(new FolderChange(FolderChangeKind.Renamed, replacement.Reference, previous.Reference, RequiresRefresh: false));

		await WaitUntilAsync(() => session.Items.Count is 1 && session.Selection.SelectedKeys.Single() == replacementKey);

		Assert.AreEqual(replacementKey, session.Selection.FocusedKey);
		Assert.AreEqual(replacementKey, session.Selection.AnchorKey);
	}

	[TestMethod]
	public async Task DeletedItemIsRemovedFromSelection()
	{
		var factory = new TestModelFactory();
		var source = new TestFolderChangeSource();
		var locationModel = factory.CreateModel("folder", "Folder", out _, source);
		var item = factory.CreateModel("item", "Item", out _);
		var resolver = CreateIncrementalResolver(locationModel, item, item.Reference, items: [item]);
		using var session = new BrowseSession(resolver);
		var selectionChanged = 0;
		session.SelectionChanged += (_, _) => selectionChanged++;

		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		var key = item.Reference.GetKey();
		session.SetSelection([key], key, key);
		source.RaiseChange(new FolderChange(FolderChangeKind.Deleted, null, item.Reference, RequiresRefresh: false));

		await WaitUntilAsync(() => session.Items.Count is 0 && session.Selection.SelectedKeys.Count is 0);

		Assert.IsEmpty(session.Selection.SelectedKeys);
		Assert.IsNull(session.Selection.FocusedKey);
		Assert.IsNull(session.Selection.AnchorKey);
		Assert.AreEqual(2, selectionChanged);
	}

	[TestMethod]
	public async Task RenamedItemMovesWhenItsNameChangesSortPosition()
	{
		var factory = new TestModelFactory();
		var source = new TestFolderChangeSource();
		var locationModel = factory.CreateModel("folder", "Folder", out _, source);
		var previous = factory.CreateModel("first", "Beta", out _);
		var other = factory.CreateModel("second", "Gamma", out _);
		var replacement = factory.CreateModel("first", "Zulu", out _);
		var resolver = CreateIncrementalResolver(locationModel, replacement, replacement.Reference, items: [previous, other]);
		using var session = new BrowseSession(resolver);
		var itemChanges = new List<BrowseItemChange>();
		session.ItemsChanged += (_, args) => itemChanges.AddRange(args.Changes);

		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		source.RaiseChange(new FolderChange(FolderChangeKind.Renamed, replacement.Reference, previous.Reference, RequiresRefresh: false));

		await WaitUntilAsync(() => session.Items.Count is 2 && ReferenceEquals(session.Items[1], replacement) && itemChanges.OfType<BrowseItemMoved>().Any());

		var moved = itemChanges.OfType<BrowseItemMoved>().Last();
		Assert.AreEqual(0, moved.PreviousIndex);
		Assert.AreEqual(1, moved.CurrentIndex);
		Assert.AreEqual(replacement.Reference.GetKey(), moved.Key);
	}

	[TestMethod]
	public async Task SameNameItemsHaveStableIdentityOrder()
	{
		var factory = new TestModelFactory();
		var locationModel = factory.CreateModel("folder", "Folder", out _);
		var later = factory.CreateModel("z-item", "Same", out _);
		var earlier = factory.CreateModel("a-item", "Same", out _);
		var resolver = new TestBrowseLocationResolver([later, earlier])
		{
			LocationModelFactory = _ => locationModel,
		};
		using var session = new BrowseSession(resolver);

		await session.NavigateAsync(new FolderLocation(locationModel.Reference));

		Assert.AreSame(earlier, session.Items[0]);
		Assert.AreSame(later, session.Items[1]);
	}

	[TestMethod]
	public async Task SortChangePublishesOneConsistentReset()
	{
		var factory = new TestModelFactory();
		var locationModel = factory.CreateModel("folder", "Folder", out _);
		var first = factory.CreateModel("first", "Alpha", out _);
		var second = factory.CreateModel("second", "Beta", out _);
		var third = factory.CreateModel("third", "Gamma", out _);
		var resolver = new TestBrowseLocationResolver([first, second, third])
		{
			LocationModelFactory = _ => locationModel,
		};
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		var changes = new List<BrowseItemChange>();
		session.ItemsChanged += (_, args) => changes.AddRange(args.Changes);

		await session.UpdateViewSettingsAsync(new BrowseViewSettings(sortPropertyId: "name", sortDirection: ViewSortDirection.Descending));

		Assert.AreEqual(1, changes.Count);
		Assert.IsInstanceOfType<BrowseItemsReset>(changes[0]);
		CollectionAssert.AreEqual(new[] {third, second, first}, session.Items.ToArray());
	}

	[TestMethod]
	public async Task SortingKeepsFoldersBeforeFilesInBothDirections()
	{
		var factory = new TestModelFactory();
		var locationModel = factory.CreateModel("folder", "Folder", out _);
		var modelFactory = new StorableModelFactory();
		var firstFolder = modelFactory.Create(factory.Source, new TestFolder("folder-a", "Alpha folder"));
		var secondFolder = modelFactory.Create(factory.Source, new TestFolder("folder-z", "Zulu folder"));
		var firstFile = factory.CreateModel("file-a", "Alpha file", out _);
		var secondFile = factory.CreateModel("file-z", "Zulu file", out _);
		var resolver = new TestBrowseLocationResolver([secondFile, secondFolder, firstFile, firstFolder])
		{
			LocationModelFactory = _ => locationModel,
		};
		using var session = new BrowseSession(resolver);

		await session.NavigateAsync(new FolderLocation(locationModel.Reference));
		Assert.IsTrue(session.Items.Take(2).All(static item => item is IFolderModel));
		Assert.IsTrue(session.Items.Skip(2).All(static item => item is not IFolderModel));

		await session.UpdateViewSettingsAsync(new BrowseViewSettings(sortPropertyId: "name", sortDirection: ViewSortDirection.Descending));
		Assert.IsTrue(session.Items.Take(2).All(static item => item is IFolderModel));
		Assert.IsTrue(session.Items.Skip(2).All(static item => item is not IFolderModel));
	}

	[TestMethod]
	public async Task GroupingChangeDoesNotResetTheCoreItemProjection()
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
		var projection = session.Items;
		var changes = new List<BrowseItemChange>();
		session.ItemsChanged += (_, args) => changes.AddRange(args.Changes);

		await session.UpdateViewSettingsAsync(new BrowseViewSettings(groupPropertyId: "System.ItemTypeText"));

		Assert.AreSame(projection, session.Items);
		Assert.IsEmpty(changes);
	}

	[TestMethod]
	public async Task SubscriberFailureDoesNotCorruptCommittedNavigation()
	{
		var factory = new TestModelFactory();
		var locationModel = factory.CreateModel("folder", "Folder", out _);
		var item = factory.CreateModel("item", "Item", out var itemCore);
		var resolver = new TestBrowseLocationResolver([item])
		{
			LocationModelFactory = _ => locationModel,
		};
		using var session = new BrowseSession(resolver);
		var laterHandlerCalled = false;
		session.ItemsChanged += (_, _) => throw new InvalidOperationException("subscriber failed");
		session.ItemsChanged += (_, _) => laterHandlerCalled = true;

		await session.NavigateAsync(new FolderLocation(locationModel.Reference));

		Assert.AreSame(item, session.Items.Single());
		Assert.IsFalse(itemCore.IsDisposed);
		Assert.AreSame(resolver.OpenedContexts.Single(), session.Context);
		Assert.IsTrue(laterHandlerCalled);
	}

	[TestMethod]
	public async Task FullRefreshKeepsOnlySelectionKeysStillPresent()
	{
		var factory = new TestModelFactory();
		var firstSource = new TestFolderChangeSource();
		var secondSource = new TestFolderChangeSource();
		var firstLocation = factory.CreateModel("first", "First", out _, firstSource);
		var secondLocation = factory.CreateModel("second", "Second", out _, secondSource);
		var selected = factory.CreateModel("selected", "Selected", out _);
		var removed = factory.CreateModel("removed", "Removed", out _);
		var retained = factory.CreateModel("retained", "Retained", out _);
		var refreshedRetained = factory.CreateModel("retained", "Retained", out _);
		var locationModels = new Queue<IStorableModel>([firstLocation, secondLocation]);
		var resolver = new TestBrowseLocationResolver([selected, removed, retained])
		{
			LocationModelFactory = _ => locationModels.Dequeue(),
		};
		using var session = new BrowseSession(resolver);

		await session.NavigateAsync(new FolderLocation(firstLocation.Reference));
		var selectedKey = selected.Reference.GetKey();
		var removedKey = removed.Reference.GetKey();
		var retainedKey = retained.Reference.GetKey();
		session.SetSelection([selectedKey, removedKey, retainedKey], removedKey, selectedKey);
		resolver.Items.Clear();
		resolver.Items.Add(refreshedRetained);
		firstSource.RaiseChange(new FolderChange(FolderChangeKind.DirectoryUpdated, null, null, RequiresRefresh: false));

		await WaitUntilAsync(() => session.Items.Count is 1 && ReferenceEquals(session.Items[0], refreshedRetained));

		Assert.AreEqual(retainedKey, session.Selection.SelectedKeys.Single());
		Assert.IsNull(session.Selection.FocusedKey);
		Assert.IsNull(session.Selection.AnchorKey);
	}

	[TestMethod]
	public async Task ViewSettingsArePersistedByBrowseLocation()
	{
		var factory = new TestModelFactory();
		using var model = factory.CreateModel("folder", "Folder", out _);
		var location = new FolderLocation(model.Reference);
		var settingsStore = new TestViewSettingsStore();
		using var session = new BrowseSession(new TestBrowseLocationResolver([]), settingsStore);

		await session.NavigateAsync(location);
		var settings = new BrowseViewSettings(ViewLayoutMode.List, sortPropertyId: "name");
		await session.UpdateViewSettingsAsync(settings);

		Assert.AreSame(settings, session.ViewSettings);
		Assert.AreSame(settings, await settingsStore.GetAsync(location));
	}

	private static TestBrowseLocationResolver CreateIncrementalResolver(
		IStorableModel locationModel,
		IStorableModel resolvedModel,
		StorableReference reference,
		IEnumerable<IStorableModel>? items = null,
		Func<StorableReference, CancellationToken, ValueTask<IStorableModel>>? itemResolver = null)
	{
		var resolver = new TestBrowseLocationResolver(items ?? [])
		{
			LocationModelFactory = _ => locationModel,
			ItemResolver = itemResolver ?? ((_, _) => ValueTask.FromResult(resolvedModel)),
		};

		return resolver;
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

	private sealed class TestFolder : IFolder
	{
		public string Id { get; }

		public string Name { get; }

		public TestFolder(string id, string name)
		{
			Id = id;
			Name = name;
		}

		public async IAsyncEnumerable<IStorableChild> GetItemsAsync(StorableType type = StorableType.All, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			await Task.CompletedTask.ConfigureAwait(false);
			yield break;
		}
	}
}

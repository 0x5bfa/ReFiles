// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Files.Adapters;
using Files.Core.Browsing;
using Files.Core.Data;
using Files.Core.ItemFeatures;
using Files.Core.ItemFeatures.Properties;
using Files.Core.Models;
using Files.Core.Sessions;
using Files.Core.Storage;
using Files.Core.ViewSettings;
using Files.Infrastructure;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OwlCore.Storage;

namespace Files.PresentationTests;

[TestClass]
public sealed class BrowsePresentationPipelineTests
{
	[TestMethod]
	[DataRow(100)]
	[DataRow(1_000)]
	[DataRow(10_000)]
	[DataRow(44_000)]
	public async Task PresentsFirstRowsBeforeEnumerationCompletes(int itemCount)
	{
		var providerPaused = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var providerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var resolver = new PresentationBrowseLocationResolver(itemCount, async (index, cancellationToken) =>
		{
			if (index is 32)
			{
				providerPaused.TrySetResult(true);
				await providerRelease.Task.WaitAsync(cancellationToken);
			}
		});
		var session = new BrowseSession(resolver);
		await using var pane = new BrowsePaneSession(session, new BrowsePreviewModel(session));
		await using var workspace = new PresentationStorageWorkspace();
		var dispatcher = new ManualDispatcher();
		await using var adapter = new BrowsePresentationAdapter(pane, workspace, dispatcher, new NullPrefetchCoordinator(), CreateText());
		var maximumItemsPerUpdate = 0;
		var itemUpdateCount = 0;
		adapter.Updated += (_, args) =>
		{
			var itemChanges = args.ItemChanges.Sum(GetItemCount);
			maximumItemsPerUpdate = Math.Max(maximumItemsPerUpdate, itemChanges);
			if (itemChanges is not 0)
			{
				itemUpdateCount++;
			}
		};
		dispatcher.DrainAll();

		var navigation = adapter.InitializeAsync();
		await providerPaused.Task.WaitAsync(TimeSpan.FromSeconds(5));
		dispatcher.DrainAll();

		Assert.IsFalse(navigation.IsCompleted);
		Assert.AreEqual(32, adapter.Items.Count);
		Assert.AreEqual("Item 00000", adapter.Items[0].Name);
		Assert.IsTrue(adapter.DetailsColumns.Count > 0);

		providerRelease.TrySetResult(true);
		await navigation;
		dispatcher.DrainAll();

		Assert.AreEqual(itemCount, adapter.Items.Count);
		Assert.IsTrue(maximumItemsPerUpdate <= 128);
		Assert.IsTrue(itemUpdateCount < Math.Max(3, itemCount / 16));
		Assert.IsTrue(dispatcher.EnqueueCount < Math.Max(8, itemCount / 16));
		Assert.IsTrue(dispatcher.MaximumCallbackDuration < TimeSpan.FromMilliseconds(100));
	}

	[TestMethod]
	public void BulkCollectionPublishesOneNotificationForOneUiBatch()
	{
		var collection = new BulkObservableCollection<int>();
		var collectionChangeCount = 0;
		NotifyCollectionChangedEventArgs? lastChange = null;
		collection.CollectionChanged += (_, args) =>
		{
			collectionChangeCount++;
			lastChange = args;
		};

		collection.AddRange(Enumerable.Range(0, 128));

		Assert.AreEqual(1, collectionChangeCount);
		Assert.AreEqual(NotifyCollectionChangedAction.Add, lastChange?.Action);
		Assert.AreEqual(128, lastChange?.NewItems?.Count);
	}

	[TestMethod]
	public async Task ProgressivePropertyEnrichmentUpdatesTheExistingRow()
	{
		var propertyReturned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var resolver = new PresentationBrowseLocationResolver(
			2,
			static (_, _) => ValueTask.CompletedTask,
			(index, _, _) =>
			{
				propertyReturned.TrySetResult(true);

				return ValueTask.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>
				{
					["System.Size"] = index,
				});
			});
		var session = new BrowseSession(resolver);
		await using var pane = new BrowsePaneSession(session, new BrowsePreviewModel(session));
		await using var workspace = new PresentationStorageWorkspace();
		var dispatcher = new ManualDispatcher();
		await using var adapter = new BrowsePresentationAdapter(pane, workspace, dispatcher, text: CreateText());
		var settings = new BrowseViewSettings(columns: [new ViewColumnSettings("System.Size", 120, 0)]);

		await adapter.InitializeAsync();
		dispatcher.DrainAll();
		await session.UpdateViewSettingsAsync(settings);
		dispatcher.DrainAll();
		var firstRow = adapter.Items[0];
		var propertyChangeCount = 0;
		firstRow.PropertyChanged += (_, args) =>
		{
			if (args.PropertyName is nameof(firstRow.Properties))
			{
				propertyChangeCount++;
			}
		};

		adapter.UpdateViewport(new BrowseViewport(0, 2));
		await propertyReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await dispatcher.WaitForWorkAsync(TimeSpan.FromSeconds(5));
		dispatcher.DrainAll();

		Assert.AreSame(firstRow, adapter.Items[0]);
		Assert.AreEqual(0, firstRow.Properties["System.Size"]);
		Assert.AreEqual(1, propertyChangeCount);
	}

	private static BrowsePresentationText CreateText()
	{
		return new BrowsePresentationText("Home", "Loading", "{0} item", "{0} items", "{0} is not a folder");
	}

	private static int GetItemCount(BrowseItemViewModelChange change)
	{
		return change switch
		{
			BrowseItemViewModelsAdded added => added.Items.Count,
			BrowseItemViewModelsReset reset => reset.Items.Count,
			_ => 1,
		};
	}

	private sealed class ManualDispatcher : IUIDispatcher
	{
		private readonly ConcurrentQueue<Action> _callbacks = new();
		private long _maximumCallbackTicks;
		private int _enqueueCount;

		public bool HasThreadAccess => true;

		public int EnqueueCount => Volatile.Read(ref _enqueueCount);

		public TimeSpan MaximumCallbackDuration => TimeSpan.FromTicks(Volatile.Read(ref _maximumCallbackTicks));

		public bool TryEnqueue(Action callback)
		{
			ArgumentNullException.ThrowIfNull(callback);

			Interlocked.Increment(ref _enqueueCount);
			_callbacks.Enqueue(callback);

			return true;
		}

		public bool TryEnqueue(DispatcherQueuePriority priority, Action callback)
		{
			return TryEnqueue(callback);
		}

		public void DrainAll()
		{
			var callbackCount = 0;
			while (_callbacks.TryDequeue(out var callback))
			{
				var startTimestamp = Stopwatch.GetTimestamp();
				callback();
				var elapsedTicks = Stopwatch.GetElapsedTime(startTimestamp).Ticks;
				UpdateMaximum(ref _maximumCallbackTicks, elapsedTicks);
				callbackCount++;
				if (callbackCount > 100_000)
				{
					throw new InvalidOperationException("The presentation dispatcher did not drain.");
				}
			}
		}

		public async Task WaitForWorkAsync(TimeSpan timeout)
		{
			var deadline = DateTime.UtcNow + timeout;
			while (_callbacks.IsEmpty && DateTime.UtcNow < deadline)
			{
				await Task.Yield();
			}

			Assert.IsFalse(_callbacks.IsEmpty);
		}

		private static void UpdateMaximum(ref long target, long candidate)
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

	private sealed class NullPrefetchCoordinator : IBrowsePrefetchCoordinator
	{
		public void UpdateViewport(BrowseViewport viewport, BrowseViewSettings settings, long browseGeneration)
		{
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

	private sealed class PresentationBrowseLocationResolver(
		int itemCount,
		Func<int, CancellationToken, ValueTask> beforeYieldAsync,
		Func<int, PropertyRequest, CancellationToken, ValueTask<IReadOnlyDictionary<string, object?>>>? getPropertiesAsync = null) : IBrowseLocationResolver
	{
		private readonly int _itemCount = itemCount;
		private readonly Func<int, CancellationToken, ValueTask> _beforeYieldAsync = beforeYieldAsync;
		private readonly Func<int, PropertyRequest, CancellationToken, ValueTask<IReadOnlyDictionary<string, object?>>>? _getPropertiesAsync = getPropertiesAsync;
		private readonly PresentationStorageSource _source = new();

		public ValueTask<IBrowseLocationContext> OpenAsync(BrowseLocation location, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(location);
			cancellationToken.ThrowIfCancellationRequested();

			return ValueTask.FromResult<IBrowseLocationContext>(new PresentationBrowseLocationContext(location, _itemCount, _source, _beforeYieldAsync, _getPropertiesAsync));
		}
	}

	private sealed class PresentationBrowseLocationContext(
		BrowseLocation location,
		int itemCount,
		PresentationStorageSource source,
		Func<int, CancellationToken, ValueTask> beforeYieldAsync,
		Func<int, PropertyRequest, CancellationToken, ValueTask<IReadOnlyDictionary<string, object?>>>? getPropertiesAsync) : IBrowseLocationContext
	{
		private readonly int _itemCount = itemCount;
		private readonly PresentationStorageSource _source = source;
		private readonly Func<int, CancellationToken, ValueTask> _beforeYieldAsync = beforeYieldAsync;
		private readonly Func<int, PropertyRequest, CancellationToken, ValueTask<IReadOnlyDictionary<string, object?>>>? _getPropertiesAsync = getPropertiesAsync;

		public BrowseLocation Location { get; } = location;

		public IStorableModel? LocationModel => null;

		public async IAsyncEnumerable<IStorableModel> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			for (var index = 0; index < _itemCount; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await _beforeYieldAsync(index, cancellationToken);
				var coreModel = new PresentationStorable($"item-{index:D5}", $"Item {index:D5}", index, _getPropertiesAsync);
				var reference = new StorableReference(_source.SourceId, coreModel.Id, new StorageAddress("presentation", coreModel.Id));
				var context = new ItemContext(_source, coreModel, reference);

				yield return new StorableModel(coreModel, reference, ItemFeatureRegistry.Empty.CreateFeatures(context));
				await Task.Yield();
			}
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

	private sealed class PresentationStorageWorkspace : IStorageWorkspace
	{
		public IReadOnlyList<IStorageSource> Sources => [];

		public async IAsyncEnumerable<IFolderModel> GetRootsAsync(StorageSourceId sourceId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			await Task.CompletedTask;
			yield break;
		}

		public ValueTask<IStorableModel> ResolveAsync(StorageSourceId sourceId, StorageAddress address, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask<IStorableModel> ResolveAsync(StorageAddress address, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask<IStorableModel> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

	private sealed class PresentationStorageSource : IStorageSource
	{
		public StorageSourceId SourceId { get; } = new("presentation");

		public string SourceType => "presentation";

		public string DisplayName => "Presentation";

		public async IAsyncEnumerable<IFolder> GetRootsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			await Task.CompletedTask;
			yield break;
		}

		public bool CanResolve(StorageAddress address) => false;

		public ValueTask<IStorable> ResolveAsync(StorageAddress address, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask<IStorable> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

	private sealed class PresentationStorable(
		string id,
		string name,
		int index,
		Func<int, PropertyRequest, CancellationToken, ValueTask<IReadOnlyDictionary<string, object?>>>? getPropertiesAsync) : IStorable, IPropertySource
	{
		private readonly int _index = index;
		private readonly Func<int, PropertyRequest, CancellationToken, ValueTask<IReadOnlyDictionary<string, object?>>>? _getPropertiesAsync = getPropertiesAsync;

		public string Id { get; } = id;

		public string Name { get; } = name;

		public ValueTask<IReadOnlyDictionary<string, object?>> GetPropertiesAsync(PropertyRequest request, CancellationToken cancellationToken = default)
		{
			return _getPropertiesAsync is null
				? ValueTask.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>())
				: _getPropertiesAsync(_index, request, cancellationToken);
		}
	}
}

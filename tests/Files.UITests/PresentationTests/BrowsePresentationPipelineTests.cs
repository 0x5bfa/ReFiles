// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Files.Adapters;
using Files.Commands;
using Files.Core.Browsing;
using Files.Core.Data;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Properties;
using Files.Core.Models;
using Files.Core.Sessions;
using Files.Core.Storage;
using Files.Core.ViewSettings;
using Files.Infrastructure;
using Files.Presentation;
using Files.Settings;
using Files.StorageOperations;
using Files.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using OwlCore.Storage;

namespace Files.UITests;

/// <summary>
/// Verifies incremental browse presentation, navigation, and command behavior.
/// </summary>
[TestClass]
public sealed class BrowsePresentationPipelineTests
{
	private const int DefaultDisposalStressIterationCount = 20;
	private const int MaximumDisposalStressIterationCount = 1_000;

	/// <summary>
	/// Verifies that the first rows are presented before item enumeration completes.
	/// </summary>
	/// <param name="itemCount">The number of items to enumerate.</param>
	/// <returns>A task that represents the asynchronous test operation.</returns>
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
		Assert.AreEqual(itemCount, adapter.CreatedItemViewModelCount);
		Assert.IsTrue(maximumItemsPerUpdate <= 128);
		Assert.IsTrue(itemUpdateCount < Math.Max(3, itemCount / 16));
		Assert.IsTrue(dispatcher.EnqueueCount < Math.Max(8, itemCount / 16));
		Assert.IsTrue(dispatcher.MaximumCallbackDuration < TimeSpan.FromMilliseconds(100));
	}

	/// <summary>
	/// Verifies that the status bar keeps showing the item count while a folder is loading.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[TestMethod]
	public async Task StatusTextKeepsItemCountWhileLoading()
	{
		var providerPaused = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var providerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var resolver = new PresentationBrowseLocationResolver(1, async (_, cancellationToken) =>
		{
			providerPaused.TrySetResult(true);
			await providerRelease.Task.WaitAsync(cancellationToken);
		});
		var session = new BrowseSession(resolver);
		await using var pane = new BrowsePaneSession(session, new BrowsePreviewModel(session));
		await using var workspace = new PresentationStorageWorkspace();
		var dispatcher = new ManualDispatcher();
		await using var adapter = new BrowsePresentationAdapter(pane, workspace, dispatcher, new NullPrefetchCoordinator(), CreateText());
		dispatcher.DrainAll();

		var navigation = adapter.InitializeAsync();
		await providerPaused.Task.WaitAsync(TimeSpan.FromSeconds(5));
		dispatcher.DrainAll();
		try
		{
			Assert.IsTrue(adapter.IsLoading);
			Assert.AreEqual(0, adapter.Items.Count);
			Assert.AreEqual("0 items", adapter.StatusText);
		}
		finally
		{
			providerRelease.TrySetResult(true);
		}

		await navigation;
		dispatcher.DrainAll();
		Assert.AreEqual("1 item", adapter.StatusText);
	}

	/// <summary>
	/// Verifies that pending items from a canceled navigation generation are discarded.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[TestMethod]
	public async Task NavigationDiscardsPendingItemsFromCanceledGeneration()
	{
		var interruptedNavigationPaused = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var resolver = new NavigationPresentationBrowseLocationResolver(interruptedNavigationPaused);
		var session = new BrowseSession(resolver);
		await using var pane = new BrowsePaneSession(session, new BrowsePreviewModel(session));
		await using var workspace = new PresentationStorageWorkspace();
		var dispatcher = new ManualDispatcher();
		await using var adapter = new BrowsePresentationAdapter(pane, workspace, dispatcher, new NullPrefetchCoordinator(), CreateText());

		await adapter.InitializeAsync();
		dispatcher.DrainAll();

		var interruptedNavigation = adapter.NavigateToReferenceAsync(CreateReference("interrupted"));
		await interruptedNavigationPaused.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var destinationNavigation = adapter.NavigateToReferenceAsync(CreateReference("destination"));
		await Assert.ThrowsAsync<OperationCanceledException>(async () => await interruptedNavigation);
		await destinationNavigation;

		var staleItemsPublished = false;
		adapter.Updated += (_, args) =>
		{
			if (args.Flags.HasFlag(BrowseUpdateFlags.Items) && adapter.Items.Any(static item => !item.Name.StartsWith("Destination ", StringComparison.Ordinal)))
			{
				staleItemsPublished = true;
			}
		};
		dispatcher.DrainAll();

		Assert.IsFalse(staleItemsPublished);
		Assert.AreEqual(300, adapter.Items.Count);
		Assert.IsTrue(adapter.Items.All(static item => item.Name.StartsWith("Destination ", StringComparison.Ordinal)));
	}

	/// <summary>
	/// Verifies that repeated navigation to the same location shares one operation.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[TestMethod]
	public async Task RepeatedNavigationToSameLocationUsesSingleOperation()
	{
		var navigationPaused = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var navigationRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var resolver = new RepeatedNavigationBrowseLocationResolver(navigationPaused, navigationRelease);
		var session = new BrowseSession(resolver);
		await using var pane = new BrowsePaneSession(session, new BrowsePreviewModel(session));
		await using var workspace = new PresentationStorageWorkspace();
		var dispatcher = new ManualDispatcher();
		await using var adapter = new BrowsePresentationAdapter(pane, workspace, dispatcher, new NullPrefetchCoordinator(), CreateText());

		await adapter.InitializeAsync();
		dispatcher.DrainAll();

		var reference = CreateReference("repeated");
		using var firstCallerCancellation = new CancellationTokenSource();
		var firstNavigation = adapter.NavigateToReferenceAsync(reference, firstCallerCancellation.Token);
		await navigationPaused.Task.WaitAsync(TimeSpan.FromSeconds(5));
		firstCallerCancellation.Cancel();
		await Assert.ThrowsAsync<OperationCanceledException>(async () => await firstNavigation);
		var repeatedNavigations = Enumerable.Range(0, 64).Select(_ => adapter.NavigateToReferenceAsync(reference)).ToArray();
		navigationRelease.TrySetResult(true);

		await Task.WhenAll(repeatedNavigations);
		await adapter.NavigateToReferenceAsync(reference);
		dispatcher.DrainAll();

		Assert.AreEqual(1, resolver.RepeatedLocationOpenCount);
		Assert.AreEqual(300, adapter.Items.Count);
		Assert.IsTrue(adapter.Items.All(static item => item.Name.StartsWith("Repeated ", StringComparison.Ordinal)));
	}

	/// <summary>
	/// Verifies that disposing the adapter waits for shared navigation cleanup.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[TestMethod]
	public async Task DisposingAdapterWaitsForSharedNavigationCleanup()
	{
		var navigationPaused = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var cancellationObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var cleanupRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var resolver = new DisposalNavigationBrowseLocationResolver(navigationPaused, cancellationObserved, cleanupRelease);
		var session = new BrowseSession(resolver);
		await using var pane = new BrowsePaneSession(session, new BrowsePreviewModel(session));
		await using var workspace = new PresentationStorageWorkspace();
		var dispatcher = new ManualDispatcher();
		var adapter = new BrowsePresentationAdapter(pane, workspace, dispatcher, new NullPrefetchCoordinator(), CreateText());
		Task? disposal = null;
		try
		{
			await adapter.InitializeAsync();
			var navigation = adapter.NavigateToReferenceAsync(CreateReference("disposal"));
			await navigationPaused.Task.WaitAsync(TimeSpan.FromSeconds(5));
			disposal = adapter.DisposeAsync().AsTask();
			await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

			Assert.IsFalse(disposal.IsCompleted);

			cleanupRelease.TrySetResult(true);
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await navigation);
			await disposal.WaitAsync(TimeSpan.FromSeconds(5));
		}
		finally
		{
			cleanupRelease.TrySetResult(true);
			if (disposal is not null)
			{
				await disposal;
			}
			else
			{
				await adapter.DisposeAsync();
			}
		}
	}

	/// <summary>
	/// Verifies that disposal at different enumeration checkpoints cancels loading and suppresses queued presentation callbacks.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[TestMethod]
	[TestCategory("Stress")]
	public async Task DisposalAtEnumerationCheckpointsSuppressesLateUpdates()
	{
		var iterationCount = ReadDisposalStressIterationCount();
		var checkpoints = new[] { 0, 31, 127, 255 };
		for (var iteration = 0; iteration < iterationCount; iteration++)
		{
			var checkpoint = checkpoints[iteration % checkpoints.Length];
			var enumerationPaused = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var enumerationRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var resolver = new PresentationBrowseLocationResolver(256, async (index, cancellationToken) =>
			{
				if (index == checkpoint)
				{
					enumerationPaused.TrySetResult(true);
					await enumerationRelease.Task.WaitAsync(cancellationToken);
				}
			});
			var session = new BrowseSession(resolver);
			await using var pane = new BrowsePaneSession(session, new BrowsePreviewModel(session));
			await using var workspace = new PresentationStorageWorkspace();
			var dispatcher = new ManualDispatcher();
			var adapter = new BrowsePresentationAdapter(pane, workspace, dispatcher, new NullPrefetchCoordinator(), CreateText());
			var disposalStarted = false;
			var lateUpdateCount = 0;
			adapter.Updated += (_, _) =>
			{
				if (disposalStarted)
				{
					lateUpdateCount++;
				}
			};

			var initialization = adapter.InitializeAsync();
			await enumerationPaused.Task.WaitAsync(TimeSpan.FromSeconds(5));
			disposalStarted = true;
			var disposal = adapter.DisposeAsync().AsTask();
			enumerationRelease.TrySetResult(true);
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await initialization);
			await disposal.WaitAsync(TimeSpan.FromSeconds(5));
			dispatcher.DrainAll();

			Assert.AreEqual(0, lateUpdateCount, $"Observed a late update at iteration {iteration} and checkpoint {checkpoint}.");
		}
	}

	/// <summary>
	/// Verifies that one UI batch produces one bulk collection notification.
	/// </summary>
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

	/// <summary>
	/// Verifies that progressive property enrichment updates the existing row.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
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

	/// <summary>
	/// Verifies that sorting reuses existing item view models.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[TestMethod]
	public async Task SortResetReusesExistingItemViewModels()
	{
		var resolver = new PresentationBrowseLocationResolver(2, static (_, _) => ValueTask.CompletedTask);
		var session = new BrowseSession(resolver);
		await using var pane = new BrowsePaneSession(session, new BrowsePreviewModel(session));
		await using var workspace = new PresentationStorageWorkspace();
		var dispatcher = new ManualDispatcher();
		await using var adapter = new BrowsePresentationAdapter(pane, workspace, dispatcher, new NullPrefetchCoordinator(), CreateText());

		await adapter.InitializeAsync();
		dispatcher.DrainAll();
		var firstItem = adapter.Items[0];
		var secondItem = adapter.Items[1];
		var settings = new BrowseViewSettings(sortPropertyId: "name", sortDirection: ViewSortDirection.Descending);

		await session.UpdateViewSettingsAsync(settings);
		dispatcher.DrainAll();

		Assert.AreSame(secondItem, adapter.Items[0]);
		Assert.AreSame(firstItem, adapter.Items[1]);
	}

	/// <summary>
	/// Verifies that large folders do not refresh selection commands for every item batch.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task LargeFolderDoesNotRefreshSelectionCommandsForEachItemBatch()
	{
		var resolver = new PresentationBrowseLocationResolver(44_000, static (_, _) => ValueTask.CompletedTask);
		var paneFactory = new BrowsePaneSessionFactory(
			() => new BrowseSession(resolver),
			static session => new BrowsePreviewModel(session));

		await using var window = new WindowSession(paneFactory);
		await using var workspace = new PresentationStorageWorkspace();
		var storageOperations = new NoOpStorageOperationService();
		var dispatcher = new ManualDispatcher();
		var stateCalls = new CountingStateCalls();
		var commandRegistry = CreateCountingCommandRegistry(stateCalls);
		var appSettings = new AppSettingsService(new Dictionary<string, object>());
		using var operationTracker = new StorageOperationTracker();
		var presentationFactory = new WindowPresentationFactory(workspace, storageOperations, operationTracker, appSettings, dispatcher, commandRegistry);
		RootViewModel root;
		try
		{
			root = new RootViewModel(window, presentationFactory);
		}
		catch (TypeInitializationException exception) when (exception.InnerException is COMException { HResult: unchecked((int)0x80040154) })
		{
			Assert.Inconclusive("The WinAppSDK resource manager is unavailable in this test host.");

			return;
		}

		await using var rootLifetime = root;

		await window.OpenTabAsync(HomeLocation.Instance);
		dispatcher.DrainAll();
		var callsBeforeNavigation = stateCalls.Count;

		await root.InitializeAsync();
		dispatcher.DrainAll();

		var folder = root.ActiveFolderBrowser;
		Assert.IsNotNull(folder);
		Assert.AreEqual(44_000, folder.Items.Count);
		Assert.AreSame(folder.Items, folder.ItemsViewSource.Source);
		Assert.AreEqual(callsBeforeNavigation, stateCalls.Count);

		folder.SetSelection([folder.Items[0]]);
		dispatcher.DrainAll();

		Assert.IsTrue(stateCalls.Count > callsBeforeNavigation);
	}

	/// <summary>
	/// Verifies that the selection commands follow the active folder selection.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task SelectionCommandsFollowActiveFolderSelection()
	{
		var resolver = new PresentationBrowseLocationResolver(3, static (_, _) => ValueTask.CompletedTask);
		var paneFactory = new BrowsePaneSessionFactory(() => new BrowseSession(resolver), static session => new BrowsePreviewModel(session));
		await using var window = new WindowSession(paneFactory);
		await using var workspace = new PresentationStorageWorkspace();
		var dispatcher = new ManualDispatcher();
		var appSettings = new AppSettingsService(new Dictionary<string, object>());
		using var operationTracker = new StorageOperationTracker();
		var presentationFactory = new WindowPresentationFactory(workspace, new NoOpStorageOperationService(), operationTracker, appSettings, dispatcher, AppCommandRegistration.Build());
		RootViewModel root;
		try
		{
			root = new RootViewModel(window, presentationFactory);
		}
		catch (TypeInitializationException exception) when (exception.InnerException is COMException { HResult: unchecked((int)0x80040154) })
		{
			Assert.Inconclusive("The WinAppSDK resource manager is unavailable in this test host.");

			return;
		}

		await using var rootLifetime = root;
		await window.OpenTabAsync(HomeLocation.Instance);
		dispatcher.DrainAll();
		await root.InitializeAsync();
		dispatcher.DrainAll();

		Assert.IsFalse(root.SelectAllCommand.IsVisible);
		var browser = root.ActiveFolderBrowser;
		Assert.IsNotNull(browser);

		await browser.NavigateToReferenceAsync(CreateReference("selection-folder"));
		dispatcher.DrainAll();

		Assert.IsTrue(root.SelectAllCommand.IsVisible);
		Assert.IsTrue(root.SelectAllCommand.IsEnabled);
		Assert.IsTrue(root.InvertSelectionCommand.IsEnabled);
		Assert.IsFalse(root.ClearSelectionCommand.IsEnabled);

		var selectAllResult = await root.SelectAllCommand.ExecuteAsync();
		dispatcher.DrainAll();

		Assert.AreEqual(CommandExecutionStatus.Succeeded, selectAllResult.Status);
		Assert.AreEqual(3, browser.SelectedItems.Count);
		Assert.IsFalse(root.SelectAllCommand.IsEnabled);
		Assert.IsTrue(root.ClearSelectionCommand.IsEnabled);

		var invertAllResult = await root.InvertSelectionCommand.ExecuteAsync();
		dispatcher.DrainAll();

		Assert.AreEqual(CommandExecutionStatus.Succeeded, invertAllResult.Status);
		Assert.IsEmpty(browser.SelectedItems);
		Assert.IsFalse(root.ClearSelectionCommand.IsEnabled);

		var firstItem = browser.Items[0];
		browser.SetSelection([firstItem]);
		dispatcher.DrainAll();
		var invertOneResult = await root.InvertSelectionCommand.ExecuteAsync();
		dispatcher.DrainAll();

		Assert.AreEqual(CommandExecutionStatus.Succeeded, invertOneResult.Status);
		Assert.AreEqual(2, browser.SelectedItems.Count);
		Assert.IsFalse(browser.SelectedItems.Contains(firstItem));

		var clearResult = await root.ClearSelectionCommand.ExecuteAsync();
		dispatcher.DrainAll();

		Assert.AreEqual(CommandExecutionStatus.Succeeded, clearResult.Status);
		Assert.IsEmpty(browser.SelectedItems);
	}

	/// <summary>
	/// Verifies that canceling a previous command waits for its call to finish.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[TestMethod]
	public async Task CancelPreviousCommandWaitsForCanceledCallToFinish()
	{
		var dispatcher = new ManualDispatcher();
		var handler = new SerializingCommandHandler(CommandIds.NavigatePath);
		var root = (RootViewModel)RuntimeHelpers.GetUninitializedObject(typeof(RootViewModel));
		using var manager = new WindowCommandManager(root, CreateSerializingCommandRegistry(handler), dispatcher);
		var firstCall = manager.ExecuteAsync(CommandIds.NavigatePath, "first");
		await handler.FirstCallStarted.WaitAsync(TimeSpan.FromSeconds(5));
		var secondCall = manager.ExecuteAsync(CommandIds.NavigatePath, "second");
		await handler.FirstCallCancellationObserved.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			Assert.AreEqual(1, handler.InvocationCount);
			Assert.AreEqual(1, handler.MaximumConcurrency);
		}
		finally
		{
			handler.AllowFirstCallToFinish();
		}

		var results = await Task.WhenAll(firstCall, secondCall).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.AreEqual(CommandExecutionStatus.Canceled, results[0].Status);
		Assert.AreEqual(CommandExecutionStatus.Succeeded, results[1].Status);
		Assert.AreEqual(2, handler.InvocationCount);
		Assert.AreEqual(1, handler.MaximumConcurrency);
	}

	private static int ReadDisposalStressIterationCount()
	{
		var value = Environment.GetEnvironmentVariable("FILES_DISPOSAL_STRESS_ITERATIONS");
		if (string.IsNullOrWhiteSpace(value))
		{
			return DefaultDisposalStressIterationCount;
		}

		if (!int.TryParse(value, out var iterationCount) || iterationCount < 1 || iterationCount > MaximumDisposalStressIterationCount)
		{
			throw new InvalidOperationException($"FILES_DISPOSAL_STRESS_ITERATIONS must be between 1 and {MaximumDisposalStressIterationCount}.");
		}

		return iterationCount;
	}

	private static BrowsePresentationText CreateText()
	{
		return new BrowsePresentationText("Home", "{0} item", "{0} items", "{0} is not a folder");
	}

	private static StorableReference CreateReference(string itemId)
	{
		return new StorableReference(new StorageSourceId("presentation"), itemId, new StorageAddress("presentation", itemId));
	}

	private static CommandRegistry CreateCountingCommandRegistry(CountingStateCalls stateCalls)
	{
		var builder = new CommandRegistryBuilder();
		var commandIds = typeof(CommandIds)
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(static field => field.FieldType == typeof(CommandId))
			.Select(static field => (CommandId)field.GetValue(null)!)
			.ToArray();
		for (var index = 0; index < commandIds.Length; index++)
		{
			var commandId = commandIds[index];
			builder.Register(new CommandDescriptor(commandId, commandId.Value, null, "Test", index), _ => new CountingCommandHandler(commandId, stateCalls));
		}

		return builder.Build();
	}

	private static CommandRegistry CreateSerializingCommandRegistry(SerializingCommandHandler serializingHandler)
	{
		var builder = new CommandRegistryBuilder();
		var commandIds = typeof(CommandIds)
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(static field => field.FieldType == typeof(CommandId))
			.Select(static field => (CommandId)field.GetValue(null)!)
			.ToArray();
		for (var index = 0; index < commandIds.Length; index++)
		{
			var commandId = commandIds[index];
			builder.Register(new CommandDescriptor(commandId, commandId.Value, null, "Test", index), _ => commandId == serializingHandler.Id ? serializingHandler : new NoOpCommandHandler(commandId));
		}

		return builder.Build();
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

	private sealed class CountingStateCalls
	{
		private int _count;

		public int Count => Volatile.Read(ref _count);

		public void Increment() => Interlocked.Increment(ref _count);
	}

	private sealed class CountingCommandHandler(CommandId id, CountingStateCalls stateCalls) : ICommandHandler
	{
		private readonly CountingStateCalls _stateCalls = stateCalls;

		public CommandId Id { get; } = id;

		public CommandConcurrencyPolicy ConcurrencyPolicy => CommandConcurrencyPolicy.AllowParallel;

		public CommandStateInvalidation StateDependencies => CommandStateInvalidation.Selection;

		public CommandState GetState(CommandContext context)
		{
			_stateCalls.Increment();

			return new CommandState(IsVisible: true, IsEnabled: true);
		}

		public ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(CommandExecutionResult.Succeeded());
	}

	private sealed class NoOpCommandHandler(CommandId id) : ICommandHandler
	{
		public CommandId Id { get; } = id;

		public CommandConcurrencyPolicy ConcurrencyPolicy => CommandConcurrencyPolicy.AllowParallel;

		public CommandStateInvalidation StateDependencies => CommandStateInvalidation.None;

		public CommandState GetState(CommandContext context) => new(IsVisible: true, IsEnabled: true);

		public ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default) => ValueTask.FromResult(CommandExecutionResult.Succeeded());
	}

	private sealed class SerializingCommandHandler : ICommandHandler
	{
		private readonly TaskCompletionSource<bool> _firstCallStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<bool> _firstCallCancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<bool> _firstCallRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _activeCount;
		private int _invocationCount;
		private int _maximumConcurrency;

		public CommandId Id { get; }

		public CommandConcurrencyPolicy ConcurrencyPolicy => CommandConcurrencyPolicy.CancelPrevious;

		public CommandStateInvalidation StateDependencies => CommandStateInvalidation.None;

		public Task FirstCallStarted => _firstCallStarted.Task;

		public Task FirstCallCancellationObserved => _firstCallCancellationObserved.Task;

		public int InvocationCount => Volatile.Read(ref _invocationCount);

		public int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);

		public SerializingCommandHandler(CommandId id)
		{
			Id = id;
		}

		public CommandState GetState(CommandContext context) => new(IsVisible: true, IsEnabled: true);

		public async ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
		{
			var invocation = Interlocked.Increment(ref _invocationCount);
			var activeCount = Interlocked.Increment(ref _activeCount);
			UpdateMaximum(ref _maximumConcurrency, activeCount);
			try
			{
				if (invocation is 1)
				{
					_firstCallStarted.TrySetResult(true);
					try
					{
						await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
					}
					catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
					{
						_firstCallCancellationObserved.TrySetResult(true);
						await _firstCallRelease.Task;

						throw;
					}
				}

				return CommandExecutionResult.Succeeded();
			}
			finally
			{
				Interlocked.Decrement(ref _activeCount);
			}
		}

		public void AllowFirstCallToFinish() => _firstCallRelease.TrySetResult(true);

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

	private sealed class NoOpStorageOperationService : IStorageOperationService
	{
		public bool CanHandle(StorageOperationRequest request) => false;

		public ValueTask<StorageOperationResult> ExecuteAsync(StorageOperationRequest request, IProgress<StorageOperationProgress>? progress = null, CancellationToken cancellationToken = default,
			IStorageOperationControl? operationControl = null) =>
			throw new NotSupportedException();
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

	private sealed class NavigationPresentationBrowseLocationResolver(TaskCompletionSource<bool> interruptedNavigationPaused) : IBrowseLocationResolver
	{
		private readonly TaskCompletionSource<bool> _interruptedNavigationPaused = interruptedNavigationPaused;
		private readonly PresentationStorageSource _source = new();

		public ValueTask<IBrowseLocationContext> OpenAsync(BrowseLocation location, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(location);
			cancellationToken.ThrowIfCancellationRequested();

			var context = location switch
			{
				HomeLocation => new PresentationBrowseLocationContext(location, 64, _source, static (_, _) => ValueTask.CompletedTask, null, "home", "Home"),
				FolderLocation { Folder.ItemId: "interrupted" } => new PresentationBrowseLocationContext(location, 512, _source, PauseInterruptedNavigationAsync, null, "interrupted-item", "Interrupted"),
				FolderLocation { Folder.ItemId: "destination" } => new PresentationBrowseLocationContext(location, 300, _source, static (_, _) => ValueTask.CompletedTask, null, "destination-item", "Destination"),
				_ => throw new InvalidOperationException("Unexpected presentation test location."),
			};

			return ValueTask.FromResult<IBrowseLocationContext>(context);
		}

		private async ValueTask PauseInterruptedNavigationAsync(int index, CancellationToken cancellationToken)
		{
			if (index is not 288)
			{
				return;
			}

			_interruptedNavigationPaused.TrySetResult(true);
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
		}
	}

	private sealed class RepeatedNavigationBrowseLocationResolver(TaskCompletionSource<bool> navigationPaused, TaskCompletionSource<bool> navigationRelease) : IBrowseLocationResolver
	{
		private readonly TaskCompletionSource<bool> _navigationPaused = navigationPaused;
		private readonly TaskCompletionSource<bool> _navigationRelease = navigationRelease;
		private readonly PresentationStorageSource _source = new();
		private int _repeatedLocationOpenCount;

		public int RepeatedLocationOpenCount => Volatile.Read(ref _repeatedLocationOpenCount);

		public ValueTask<IBrowseLocationContext> OpenAsync(BrowseLocation location, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(location);
			cancellationToken.ThrowIfCancellationRequested();

			var context = location switch
			{
				HomeLocation => new PresentationBrowseLocationContext(location, 64, _source, static (_, _) => ValueTask.CompletedTask, null, "home", "Home"),
				FolderLocation { Folder.ItemId: "repeated" } => CreateRepeatedContext(location),
				_ => throw new InvalidOperationException("Unexpected repeated navigation test location."),
			};

			return ValueTask.FromResult<IBrowseLocationContext>(context);
		}

		private PresentationBrowseLocationContext CreateRepeatedContext(BrowseLocation location)
		{
			Interlocked.Increment(ref _repeatedLocationOpenCount);

			return new PresentationBrowseLocationContext(location, 300, _source, PauseNavigationAsync, null, "repeated-item", "Repeated");
		}

		private async ValueTask PauseNavigationAsync(int index, CancellationToken cancellationToken)
		{
			if (index is not 32)
			{
				return;
			}

			_navigationPaused.TrySetResult(true);
			await _navigationRelease.Task.WaitAsync(cancellationToken);
		}
	}

	private sealed class DisposalNavigationBrowseLocationResolver(
		TaskCompletionSource<bool> navigationPaused,
		TaskCompletionSource<bool> cancellationObserved,
		TaskCompletionSource<bool> cleanupRelease) : IBrowseLocationResolver
	{
		private readonly TaskCompletionSource<bool> _navigationPaused = navigationPaused;
		private readonly TaskCompletionSource<bool> _cancellationObserved = cancellationObserved;
		private readonly TaskCompletionSource<bool> _cleanupRelease = cleanupRelease;
		private readonly PresentationStorageSource _source = new();

		public ValueTask<IBrowseLocationContext> OpenAsync(BrowseLocation location, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(location);
			cancellationToken.ThrowIfCancellationRequested();

			var context = location switch
			{
				HomeLocation => new PresentationBrowseLocationContext(location, 64, _source, static (_, _) => ValueTask.CompletedTask, null, "home", "Home"),
				FolderLocation { Folder.ItemId: "disposal" } => new PresentationBrowseLocationContext(location, 300, _source, PauseNavigationAsync, null, "disposal-item", "Disposal"),
				_ => throw new InvalidOperationException("Unexpected disposal navigation test location."),
			};

			return ValueTask.FromResult<IBrowseLocationContext>(context);
		}

		private async ValueTask PauseNavigationAsync(int index, CancellationToken cancellationToken)
		{
			if (index is not 32)
			{
				return;
			}

			_navigationPaused.TrySetResult(true);
			try
			{
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				_cancellationObserved.TrySetResult(true);
				await _cleanupRelease.Task;

				throw;
			}
		}
	}

	private sealed class PresentationBrowseLocationContext(
		BrowseLocation location,
		int itemCount,
		PresentationStorageSource source,
		Func<int, CancellationToken, ValueTask> beforeYieldAsync,
		Func<int, PropertyRequest, CancellationToken, ValueTask<IReadOnlyDictionary<string, object?>>>? getPropertiesAsync,
		string itemIdPrefix = "item",
		string itemNamePrefix = "Item") : IBrowseLocationContext
	{
		private readonly int _itemCount = itemCount;
		private readonly PresentationStorageSource _source = source;
		private readonly Func<int, CancellationToken, ValueTask> _beforeYieldAsync = beforeYieldAsync;
		private readonly Func<int, PropertyRequest, CancellationToken, ValueTask<IReadOnlyDictionary<string, object?>>>? _getPropertiesAsync = getPropertiesAsync;
		private readonly string _itemIdPrefix = itemIdPrefix;
		private readonly string _itemNamePrefix = itemNamePrefix;

		public BrowseLocation Location { get; } = location;

		public IStorableModel? LocationModel => null;

		public async IAsyncEnumerable<IStorableModel> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			for (var index = 0; index < _itemCount; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await _beforeYieldAsync(index, cancellationToken);
				var coreModel = new PresentationStorable($"{_itemIdPrefix}-{index:D5}", $"{_itemNamePrefix} {index:D5}", index, _getPropertiesAsync);
				var reference = new StorableReference(_source.SourceId, coreModel.Id, new StorageAddress("presentation", coreModel.Id));
				var context = new ItemContext(_source, coreModel, reference);

				yield return new StorableModel(coreModel, reference, CapabilityRegistry.Empty.CreateCapabilities(context));
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

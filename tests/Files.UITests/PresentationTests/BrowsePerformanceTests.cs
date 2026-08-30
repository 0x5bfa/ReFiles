// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Files.Commands;
using Files.Controls;
using Files.Core.Browsing;
using Files.Core.Composition;
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
using Files.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using OwlCore.Storage;

namespace Files.UITests;

/// <summary>
/// Measures the production browse pipeline through actual WinUI row realization.
/// </summary>
[TestClass]
public sealed class BrowsePerformanceTests
{
	private const int DefaultStressIterationCount = 5;
	private const int DefaultStressItemCount = 512;
	private const int MaximumStressIterationCount = 250;
	private const int MaximumStressItemCount = 44_000;
	private const int FirstPageItemCount = 32;
	private const int VirtualizationSafetyLimit = 200;
	private static readonly TimeSpan FirstContentTimeout = TimeSpan.FromSeconds(15);
	private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StressUiTimeout = TimeSpan.FromSeconds(30);

	public TestContext TestContext { get; set; } = null!;

	/// <summary>Runs deterministic synthetic browse scenarios and writes informational timing data to JSON.</summary>
	[UITestMethod]
	[TestCategory("Performance")]
	public async Task SyntheticBrowsePerformanceBaselines()
	{
		var scenarios = new[]
		{
			new SyntheticBrowseScenario("details-100", 100, EnablePropertyEnrichment: false),
			new SyntheticBrowseScenario("details-1000", 1_000, EnablePropertyEnrichment: false),
			new SyntheticBrowseScenario("details-10000", 10_000, EnablePropertyEnrichment: false),
			new SyntheticBrowseScenario("details-44000", 44_000, EnablePropertyEnrichment: false),
			new SyntheticBrowseScenario("details-44000-properties", 44_000, EnablePropertyEnrichment: true),
		};
		var results = new List<BrowsePerformanceResult>(scenarios.Length);

		foreach (var scenario in scenarios)
		{
			var result = await RunSyntheticScenarioAsync(scenario);
			results.Add(result);
			TestContext.WriteLine(JsonSerializer.Serialize(result));
		}

		var outputPath = await BrowsePerformanceResultWriter.WriteAsync("browse-synthetic", results);
		TestContext.AddResultFile(outputPath);
		TestContext.WriteLine($"Browse performance results: {outputPath}");
	}

	/// <summary>Runs an opt-in real Windows folder scenario through Windows storage/Shell integration.</summary>
	[UITestMethod]
	[TestCategory("RealFolderPerformance")]
	public async Task RealFolderBrowsePerformance()
	{
		var folderPaths = ReadRealFolderPaths();
		if (folderPaths.Count is 0)
		{
			Assert.Inconclusive("Set FILES_PERF_REAL_FOLDER or FILES_PERF_REAL_FOLDERS to opt into the machine-dependent real-folder performance scenario.");

			return;
		}

		var iterations = ReadPositiveInt32("FILES_PERF_ITERATIONS", 3);
		var results = new List<BrowsePerformanceResult>();
		foreach (var folderPath in folderPaths)
		{
			results.AddRange(await RunRealFolderScenarioAsync(folderPath, iterations));
		}

		var outputPath = await BrowsePerformanceResultWriter.WriteAsync("browse-real-folder", results);
		TestContext.AddResultFile(outputPath);
		TestContext.WriteLine($"Real-folder performance results: {outputPath}");
	}

	/// <summary>Exercises selection, virtualization, layout switching, multiple views, and teardown ordering under repeated load.</summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	[TestCategory("Stress")]
	public async Task FolderViewInteractionAndLifetimeStress()
	{
		var iterationCount = ReadBoundedPositiveInt32("FILES_UI_STRESS_ITERATIONS", DefaultStressIterationCount, MaximumStressIterationCount);
		var itemCount = ReadBoundedPositiveInt32("FILES_UI_STRESS_ITEMS", DefaultStressItemCount, MaximumStressItemCount);
		var viewModes = new[] { FolderViewMode.Details, FolderViewMode.List, FolderViewMode.Grid, FolderViewMode.Cards, FolderViewMode.Columns };
		var random = new Random(0xF01DE5);
		for (var iteration = 0; iteration < iterationCount; iteration++)
		{
			WriteStressProgress(iteration, "creating environment");
			var primaryEnvironment = await FolderViewStressEnvironment.CreateAsync(itemCount);
			FolderViewStressEnvironment? secondaryEnvironment = null;
			PerformanceWindowHost? primaryHost = null;
			PerformanceWindowHost? secondaryHost = null;
			try
			{
				secondaryEnvironment = await FolderViewStressEnvironment.CreateAsync(itemCount);
				var primaryBrowser = new FolderBrowser { ViewModel = primaryEnvironment.Folder };
				var secondaryBrowser = new FolderBrowser { ViewModel = secondaryEnvironment.Folder };
				primaryHost = await PerformanceWindowHost.ShowAsync(primaryBrowser);
				secondaryHost = await PerformanceWindowHost.ShowAsync(secondaryBrowser);
				WriteStressProgress(iteration, "windows loaded");
				foreach (var viewMode in viewModes)
				{
					WriteStressProgress(iteration, $"switching to {viewMode}");
					await primaryEnvironment.Folder.SetViewModeAsync(viewMode);
					await secondaryEnvironment.Folder.SetViewModeAsync(viewMode);
					await primaryEnvironment.Folder.SetItemSizeAsync(1 + ((iteration + (int)viewMode) % 5));
					await secondaryEnvironment.Folder.SetItemSizeAsync(1 + ((iteration + (int)viewMode + 2) % 5));
					await WaitForUiIdleAsync(StressUiTimeout);
					var primaryList = await WaitForDescendantAsync<ListViewBase>(primaryBrowser);
					var secondaryList = await WaitForDescendantAsync<ListViewBase>(secondaryBrowser);
					var selectionMode = (RectangleSelectionMode)((iteration + (int)viewMode) % 3);
					var baseline = primaryEnvironment.Folder.SelectedItems.Cast<object>().ToArray();
					var intersections = CreateRandomSelection(random, primaryEnvironment.Folder.Items, maximumCount: 16);
					var expectedSelection = new RectangleSelectionModel(baseline, selectionMode).GetSelection(intersections);
					ApplyRectangleSelection(primaryList, expectedSelection);
					await WaitForSelectionAsync(primaryEnvironment.Folder, expectedSelection.Cast<BrowseItemViewModel>());
					var secondarySelection = CreateRandomSelection(random, secondaryEnvironment.Folder.Items, maximumCount: 16);
					ApplyRectangleSelection(secondaryList, secondarySelection);
					await WaitForSelectionAsync(secondaryEnvironment.Folder, secondarySelection.Cast<BrowseItemViewModel>());

					primaryList.ScrollIntoView(primaryEnvironment.Folder.Items[random.Next(primaryEnvironment.Folder.Items.Count)]);
					secondaryList.ScrollIntoView(secondaryEnvironment.Folder.Items[random.Next(secondaryEnvironment.Folder.Items.Count)]);
					await primaryEnvironment.Folder.SetSortAsync("System.ItemNameDisplay", iteration % 2 is 0 ? ViewSortDirection.Ascending : ViewSortDirection.Descending);
					await secondaryEnvironment.Folder.SetGroupingAsync(viewMode is FolderViewMode.Cards ? "System.ItemTypeText" : null, ViewSortDirection.Ascending);
					await WaitForUiIdleAsync(StressUiTimeout);
					WriteStressProgress(iteration, $"completed {viewMode}");
				}

				var primarySelectionHost = FindDescendant<RectangleSelectionHost>(primaryBrowser);
				Assert.IsNotNull(primarySelectionHost);
				await primaryHost.CloseAsync();
				WriteStressProgress(iteration, "primary window closed");
				Assert.AreEqual(0, primarySelectionHost.TargetCount, $"Primary selection targets leaked at iteration {iteration}.");
				await primaryEnvironment.DisposeAsync();

				WriteStressProgress(iteration, "updating remaining selection");
				var remainingList = await WaitForDescendantAsync<ListViewBase>(secondaryBrowser);
				var finalSelection = CreateRandomSelection(random, secondaryEnvironment.Folder.Items, maximumCount: 8);
				ApplyRectangleSelection(remainingList, finalSelection);
				await WaitForSelectionAsync(secondaryEnvironment.Folder, finalSelection.Cast<BrowseItemViewModel>());
				WriteStressProgress(iteration, "remaining selection updated");
				var secondarySelectionHost = FindDescendant<RectangleSelectionHost>(secondaryBrowser);
				Assert.IsNotNull(secondarySelectionHost);

				await secondaryHost.CloseAsync();
				WriteStressProgress(iteration, "secondary window closed");
				Assert.AreEqual(0, secondarySelectionHost.TargetCount, $"Secondary selection targets leaked at iteration {iteration}.");
				await secondaryEnvironment.DisposeAsync();
			}
			finally
			{
				if (primaryHost is not null)
				{
					await primaryHost.CloseAsync();
				}

				if (secondaryHost is not null)
				{
					await secondaryHost.CloseAsync();
				}

				await primaryEnvironment.DisposeAsync();
				if (secondaryEnvironment is not null)
				{
					await secondaryEnvironment.DisposeAsync();
				}
			}
		}
	}

	private static async Task<BrowsePerformanceResult> RunSyntheticScenarioAsync(SyntheticBrowseScenario scenario)
	{
		var providerPaused = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var providerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var propertyObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var resolver = new PerformanceBrowseLocationResolver(
			scenario.ItemCount,
			async (index, cancellationToken) =>
			{
				if (index is 32)
				{
					providerPaused.TrySetResult(true);
					await providerRelease.Task.WaitAsync(cancellationToken);
				}
			},
				scenario.EnablePropertyEnrichment
					? (index, _, _) =>
				{
					propertyObserved.TrySetResult(true);

					return ValueTask.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?> { ["System.Size"] = (long)index });
				}
				: null);
		var settingsStore = new InMemoryViewSettingsStore();
		if (scenario.EnablePropertyEnrichment)
		{
			await settingsStore.SetAsync(HomeLocation.Instance, new BrowseViewSettings(columns: [new ViewColumnSettings("System.Size", 120, 0)]));
		}

		var session = new BrowseSession(resolver, settingsStore);
		var paneFactory = new BrowsePaneSessionFactory(() => session, static browseSession => new BrowsePreviewModel(browseSession));

		await using var coreWindow = new WindowSession(paneFactory);
		await using var workspace = new PerformanceStorageWorkspace();
		var dispatcher = new MeasuringUiDispatcher(UnitTestApp.TestDispatcherQueue);
		var storageOperations = new NoOpStorageOperationService();
		var appSettings = new AppSettingsService(new Dictionary<string, object>());
		using var operationTracker = new StorageOperationTracker();
		var presentationFactory = new WindowPresentationFactory(workspace, storageOperations, operationTracker, appSettings, dispatcher, CreateNoOpCommandRegistry());
		await coreWindow.OpenTabAsync();
		await using var root = new RootViewModel(coreWindow, presentationFactory);
		var folder = root.ActiveFolderBrowser;
		Assert.IsNotNull(folder);

		var detailsView = new DetailsFolderView { ViewModel = folder };
		using var host = await PerformanceWindowHost.ShowAsync(detailsView);
		using var tableDiagnostics = new TableViewDiagnostics(detailsView.PerformanceTable);
		await using var dispatcherProbe = new DispatcherLatencyProbe(UnitTestApp.TestDispatcherQueue);
		using var metrics = new BrowseMeasurementRecorder(session, folder, tableDiagnostics);

		dispatcherProbe.Start();
		metrics.Start();
		var navigation = root.InitializeAsync();

		try
		{
			await providerPaused.Task.WaitAsync(FirstContentTimeout);
			await metrics.FirstCoreBatch.WaitAsync(FirstContentTimeout);
			await metrics.FirstPresentedItem.WaitAsync(FirstContentTimeout);
			await metrics.FirstRealizedRow.WaitAsync(FirstContentTimeout);
			Assert.IsFalse(navigation.IsCompleted, "The first row must be realized while the synthetic provider is still paused after the initial batch.");

			if (scenario.EnablePropertyEnrichment)
			{
				folder.UpdateViewport(new BrowseViewport(0, Math.Min(32, scenario.ItemCount)));
				await propertyObserved.Task.WaitAsync(FirstContentTimeout);
			}
		}
		finally
		{
			providerRelease.TrySetResult(true);
		}

		await navigation.WaitAsync(NavigationTimeout);
		metrics.MarkEnumerationCompleted();
		await WaitForPresentedItemsAsync(folder, scenario.ItemCount);
		await dispatcherProbe.StopAsync();
		metrics.CaptureFinalState();

		Assert.AreEqual(scenario.ItemCount, folder.Items.Count);
		Assert.IsTrue(metrics.FirstRealizedRowTimestamp > 0);
		Assert.IsTrue(metrics.FirstRealizedRowTimestamp < metrics.EnumerationCompletedTimestamp);
		Assert.IsTrue(metrics.MaximumRealizedRowCount < VirtualizationSafetyLimit,
			$"Expected virtualization to keep realized rows below {VirtualizationSafetyLimit}, but observed {metrics.MaximumRealizedRowCount}.");
		Assert.IsTrue(dispatcherProbe.MaximumLatency < TimeSpan.FromMilliseconds(500), $"Observed a catastrophic UI-thread stall of {dispatcherProbe.MaximumLatency.TotalMilliseconds:F1} ms.");
		Assert.IsTrue(metrics.CollectionNotificationCount < Math.Max(8, scenario.ItemCount / 8), "Collection notifications grew too quickly relative to the folder size.");
		Assert.AreEqual(scenario.ItemCount, metrics.UniqueViewModelCount, "Progressive updates should not create replacement row view models for already-published items.");

		return metrics.CreateResult(
			scenario.Name,
			dispatcherProbe,
			dispatcher,
			path: null,
			cacheState: "synthetic",
			propertiesEnabled: scenario.EnablePropertyEnrichment,
			thumbnailsEnabled: false,
			environmentNotes: "Deterministic synthetic provider; no filesystem or Shell thumbnail work.");
	}

	private static async Task<IReadOnlyList<BrowsePerformanceResult>> RunRealFolderScenarioAsync(string folderPath, int iterations)
	{
		await using var runtime = new FilesCoreBuilder().AddWindowsStorage().Build();
		var dispatcher = new MeasuringUiDispatcher(UnitTestApp.TestDispatcherQueue);
		var coreWindow = await runtime.ShellSession.CreateWindowAsync();
		var appSettings = new AppSettingsService(new Dictionary<string, object>());
		using var operationTracker = new StorageOperationTracker();
		var presentationFactory = new WindowPresentationFactory(runtime.Workspace, runtime.StorageOperations, operationTracker, appSettings, dispatcher, CreateNoOpCommandRegistry());
		await using var root = new RootViewModel(coreWindow, presentationFactory);
		await root.InitializeAsync().WaitAsync(NavigationTimeout);

		var folder = root.ActiveFolderBrowser;
		Assert.IsNotNull(folder);
		var pane = coreWindow.ActiveTab?.ActivePane?.Content as BrowsePaneSession;
		Assert.IsNotNull(pane);

		var detailsView = new DetailsFolderView { ViewModel = folder };
		using var host = await PerformanceWindowHost.ShowAsync(detailsView);
		await WaitForUiIdleAsync();

		var results = new List<BrowsePerformanceResult>(iterations);
		for (var iteration = 0; iteration < iterations; iteration++)
		{
			using var tableDiagnostics = new TableViewDiagnostics(detailsView.PerformanceTable);
			await using var dispatcherProbe = new DispatcherLatencyProbe(UnitTestApp.TestDispatcherQueue);
			using var metrics = new BrowseMeasurementRecorder(pane.BrowseSession, folder, tableDiagnostics);
			var dispatcherCountBefore = dispatcher.EnqueueCount;
			dispatcherProbe.Start();
			metrics.Start();

			var navigation = iteration is 0 ? folder.NavigateToPathAsync(folderPath) : folder.RefreshAsync();
			await navigation.WaitAsync(NavigationTimeout);
			metrics.MarkEnumerationCompleted();
			await WaitForPresentedItemsAsync(folder, pane.BrowseSession.Items.Count);
			await metrics.FirstCoreBatch.WaitAsync(FirstContentTimeout);
			await metrics.FirstPresentedItem.WaitAsync(FirstContentTimeout);
			await metrics.FirstRealizedRow.WaitAsync(FirstContentTimeout);
			await metrics.WaitForInitialPresentationAsync(FirstContentTimeout);
			await dispatcherProbe.StopAsync();
			metrics.CaptureFinalState();

			Assert.IsTrue(metrics.FirstRealizedRowTimestamp > 0);
			Assert.IsTrue(metrics.MaximumRealizedRowCount < VirtualizationSafetyLimit, $"Expected virtualization to remain active, but observed {metrics.MaximumRealizedRowCount} realized rows.");
			Assert.IsTrue(metrics.RepeatedThumbnailUpdateCount <= metrics.ContentThumbnailPublicationCount + metrics.RepeatedFallbackThumbnailPublicationCount,
				$"Observed {metrics.RepeatedThumbnailUpdateCount} repeated UI thumbnail updates but only {metrics.ContentThumbnailPublicationCount} content upgrades and " +
				$"{metrics.RepeatedFallbackThumbnailPublicationCount} repeated fallback publications.");

			results.Add(metrics.CreateResult(
				$"real-folder-{iteration + 1}",
				dispatcherProbe,
				dispatcher,
				folderPath,
				iteration is 0 ? "unknown" : "warm",
				propertiesEnabled: true,
				thumbnailsEnabled: true,
				environmentNotes: Environment.GetEnvironmentVariable("FILES_PERF_ENVIRONMENT_NOTES"),
				dispatcherCallbackCountOverride: dispatcher.EnqueueCount - dispatcherCountBefore));
		}

		return results;
	}

	private static CommandRegistry CreateNoOpCommandRegistry()
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
			builder.Register(new CommandDescriptor(commandId, commandId.Value, null, "Performance", index), _ => new NoOpCommandHandler(commandId));
		}

		return builder.Build();
	}

	private static int ReadPositiveInt32(string name, int fallback) => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;

	private static IReadOnlyList<string> ReadRealFolderPaths()
	{
		var configuredPaths = Environment.GetEnvironmentVariable("FILES_PERF_REAL_FOLDERS");
		if (string.IsNullOrWhiteSpace(configuredPaths))
		{
			configuredPaths = Environment.GetEnvironmentVariable("FILES_PERF_REAL_FOLDER");
		}

		if (string.IsNullOrWhiteSpace(configuredPaths))
		{
			return [];
		}

		return configuredPaths.Split(Path.PathSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private static void ApplyRectangleSelection(ListViewBase target, IEnumerable<object> selection)
	{
		RectangleSelection.BeginSelectionUpdate([target]);
		try
		{
			target.SelectedItems.Clear();
			foreach (var item in selection)
			{
				target.SelectedItems.Add(item);
			}
		}
		finally
		{
			RectangleSelection.EndSelectionUpdate([target]);
		}

		RectangleSelection.RaiseSelectionUpdated([target]);
	}

	private static HashSet<object> CreateRandomSelection(Random random, IReadOnlyList<BrowseItemViewModel> items, int maximumCount)
	{
		var selection = new HashSet<object>();
		var count = random.Next(0, Math.Min(maximumCount, items.Count) + 1);
		for (var index = 0; index < count; index++)
		{
			selection.Add(items[random.Next(items.Count)]);
		}

		return selection;
	}

	private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
	{
		if (root is T match)
		{
			return match;
		}

		var pending = new Queue<DependencyObject>();
		pending.Enqueue(root);
		while (pending.TryDequeue(out var current))
		{
			var childCount = VisualTreeHelper.GetChildrenCount(current);
			for (var index = 0; index < childCount; index++)
			{
				var child = VisualTreeHelper.GetChild(current, index);
				if (child is T childMatch)
				{
					return childMatch;
				}

				pending.Enqueue(child);
			}
		}

		return null;
	}

	private static int ReadBoundedPositiveInt32(string name, int fallback, int maximum)
	{
		var value = Environment.GetEnvironmentVariable(name);
		if (string.IsNullOrWhiteSpace(value))
		{
			return fallback;
		}

		if (!int.TryParse(value, out var result) || result < 1 || result > maximum)
		{
			throw new InvalidOperationException($"{name} must be between 1 and {maximum}.");
		}

		return result;
	}

	private static async Task<T> WaitForDescendantAsync<T>(DependencyObject root) where T : FrameworkElement
	{
		var started = Stopwatch.GetTimestamp();
		while (Stopwatch.GetElapsedTime(started) < NavigationTimeout)
		{
			if (FindDescendant<T>(root) is { IsLoaded: true } descendant)
			{
				return descendant;
			}

			await WaitForUiIdleAsync(StressUiTimeout);
		}

		throw new AssertFailedException($"A loaded {typeof(T).Name} descendant was not found before the timeout.");
	}

	private static async Task WaitForSelectionAsync(FolderBrowserViewModel folder, IEnumerable<BrowseItemViewModel> expectedItems)
	{
		var expectedKeys = expectedItems.Select(static item => item.Reference.GetKey()).ToHashSet();
		var started = Stopwatch.GetTimestamp();
		while (Stopwatch.GetElapsedTime(started) < NavigationTimeout)
		{
			if (expectedKeys.SetEquals(folder.SelectedKeys))
			{
				return;
			}

			await WaitForUiIdleAsync(StressUiTimeout);
		}

		Assert.Fail($"Expected {expectedKeys.Count} selected browse items, but observed {folder.SelectedKeys.Count}.");
	}

	private static Task WaitForUnloadedAsync(FrameworkElement element)
	{
		if (!element.IsLoaded)
		{
			return Task.CompletedTask;
		}

		var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		RoutedEventHandler? handler = null;
		handler = (_, _) =>
		{
			element.Unloaded -= handler;
			completion.TrySetResult(true);
		};
		element.Unloaded += handler;

		return completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
	}

	private static void WriteStressProgress(int iteration, string phase)
	{
		Console.WriteLine($"Folder view stress iteration {iteration}: {phase}.");
		Console.Out.Flush();
	}

	private static async Task WaitForPresentedItemsAsync(FolderBrowserViewModel folder, int expectedCount)
	{
		var started = Stopwatch.GetTimestamp();
		while (folder.Items.Count != expectedCount)
		{
			if (Stopwatch.GetElapsedTime(started) >= NavigationTimeout)
			{
				Assert.Fail($"Expected {expectedCount} presented items, but observed {folder.Items.Count} before the timeout.");
			}

			await WaitForUiIdleAsync(StressUiTimeout);
		}
	}

	private static async Task WaitForUiIdleAsync(TimeSpan? timeout = null)
	{
		var idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!UnitTestApp.TestDispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => idle.TrySetResult(true)))
		{
			throw new InvalidOperationException("Could not enqueue a UI-idle marker.");
		}

		await idle.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(5));
		await Task.Yield();
	}

	private readonly record struct SyntheticBrowseScenario(string Name, int ItemCount, bool EnablePropertyEnrichment);

	private sealed class FolderViewStressEnvironment : IAsyncDisposable
	{
		private readonly WindowSession _window;
		private readonly PerformanceStorageWorkspace _workspace;
		private readonly StorageOperationTracker _operationTracker;
		private readonly RootViewModel _root;
		private int _isDisposed;

		public FolderBrowserViewModel Folder { get; }

		private FolderViewStressEnvironment(WindowSession window, PerformanceStorageWorkspace workspace, StorageOperationTracker operationTracker, RootViewModel root, FolderBrowserViewModel folder)
		{
			_window = window;
			_workspace = workspace;
			_operationTracker = operationTracker;
			_root = root;
			Folder = folder;
		}

		public static async Task<FolderViewStressEnvironment> CreateAsync(int itemCount)
		{
			var resolver = new PerformanceBrowseLocationResolver(itemCount, static (_, _) => ValueTask.CompletedTask, null);
			var paneFactory = new BrowsePaneSessionFactory(() => new BrowseSession(resolver), static session => new BrowsePreviewModel(session));
			var window = new WindowSession(paneFactory);
			var workspace = new PerformanceStorageWorkspace();
			var operationTracker = new StorageOperationTracker();
			RootViewModel? root = null;
			try
			{
				await window.OpenTabAsync(HomeLocation.Instance);
				var dispatcher = new MeasuringUiDispatcher(UnitTestApp.TestDispatcherQueue);
				var appSettings = new AppSettingsService(new Dictionary<string, object>());
				var presentationFactory = new WindowPresentationFactory(workspace, new NoOpStorageOperationService(), operationTracker, appSettings, dispatcher, CreateNoOpCommandRegistry());
				root = new RootViewModel(window, presentationFactory);
				await root.InitializeAsync().WaitAsync(NavigationTimeout);
				var folder = root.ActiveFolderBrowser ?? throw new InvalidOperationException("The stress window does not have an active folder browser.");
				await WaitForPresentedItemsAsync(folder, itemCount);

				return new FolderViewStressEnvironment(window, workspace, operationTracker, root, folder);
			}
			catch
			{
				if (root is not null)
				{
					await root.DisposeAsync();
				}

				await window.DisposeAsync();
				await workspace.DisposeAsync();
				operationTracker.Dispose();

				throw;
			}
		}

		public async ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
			{
				return;
			}

			await _root.DisposeAsync();
			await _window.DisposeAsync();
			await _workspace.DisposeAsync();
			_operationTracker.Dispose();
		}
	}

	private sealed class BrowseMeasurementRecorder : IDisposable
	{
		private readonly IBrowseSession _session;
		private readonly FolderBrowserViewModel _folder;
		private readonly TableViewDiagnostics _tableDiagnostics;
		private readonly HashSet<BrowseItemViewModel> _uniqueViewModels = new(ReferenceEqualityComparer.Instance);
		private readonly HashSet<BrowseItemViewModel> _observedItems = new(ReferenceEqualityComparer.Instance);
		private readonly Dictionary<BrowseItemViewModel, int> _thumbnailUpdateCounts = new(ReferenceEqualityComparer.Instance);
		private readonly ConcurrentDictionary<StorableKey, int> _fallbackThumbnailPublicationCounts = new();
		private readonly Dictionary<BrowseItemViewModel, long> _firstPropertyTimestamps = new(ReferenceEqualityComparer.Instance);
		private readonly Dictionary<BrowseItemViewModel, long> _firstThumbnailTimestamps = new(ReferenceEqualityComparer.Instance);
		private readonly TaskCompletionSource<bool> _firstCoreBatch = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<bool> _firstPresentedItem = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<bool> _firstRealizedRow = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private long _startTimestamp;
		private long _firstCoreBatchTimestamp;
		private long _firstPresentedItemTimestamp;
		private long _firstRealizedRowTimestamp;
		private long _enumerationCompletedTimestamp;
		private long _firstPropertiesTimestamp;
		private long _firstThumbnailTimestamp;
		private long _firstPagePropertiesCompletedTimestamp;
		private long _firstPageThumbnailsCompletedTimestamp;
		private long _initialPropertiesCompletedTimestamp;
		private long _initialThumbnailsCompletedTimestamp;
		private long _initialPresentationCompletedTimestamp;
		private int _collectionNotificationCount;
		private int _maximumRealizedRowCount;
		private int _initialPresentationItemCount;
		private int _fallbackThumbnailPublicationCount;
		private int _contentThumbnailPublicationCount;
		private long _baselineGeneration;
		private bool _started;

		public Task FirstCoreBatch => _firstCoreBatch.Task;
		public Task FirstPresentedItem => _firstPresentedItem.Task;
		public Task FirstRealizedRow => _firstRealizedRow.Task;
		public long FirstRealizedRowTimestamp => Volatile.Read(ref _firstRealizedRowTimestamp);
		public long EnumerationCompletedTimestamp => Volatile.Read(ref _enumerationCompletedTimestamp);
		public int CollectionNotificationCount => Volatile.Read(ref _collectionNotificationCount);
		public int MaximumRealizedRowCount => Volatile.Read(ref _maximumRealizedRowCount);
		public int UniqueViewModelCount => _uniqueViewModels.Count;
		public int RepeatedThumbnailUpdateCount => _thumbnailUpdateCounts.Values.Sum(static count => Math.Max(0, count - 1));
		public int RepeatedFallbackThumbnailPublicationCount => _fallbackThumbnailPublicationCounts.Values.Sum(static count => Math.Max(0, count - 1));
		public int ContentThumbnailPublicationCount => Volatile.Read(ref _contentThumbnailPublicationCount);

		public BrowseMeasurementRecorder(IBrowseSession session, FolderBrowserViewModel folder, TableViewDiagnostics tableDiagnostics)
		{
			_session = session;
			_folder = folder;
			_tableDiagnostics = tableDiagnostics;
		}

		public void Start()
		{
			if (_started)
			{
				throw new InvalidOperationException("The browse measurement has already started.");
			}

			_started = true;
			_startTimestamp = Stopwatch.GetTimestamp();
			_baselineGeneration = _session.Generation;
			_session.ItemsChanged += Session_ItemsChanged;
			_session.ItemPresentationChanged += Session_ItemPresentationChanged;
			_folder.Items.CollectionChanged += Items_CollectionChanged;
			_tableDiagnostics.RowRealized += TableDiagnostics_RowRealized;
			_tableDiagnostics.RealizedRowCountChanged += TableDiagnostics_RealizedRowCountChanged;
			foreach (var item in _folder.Items)
			{
				ObserveItem(item);
			}
		}

		public void MarkEnumerationCompleted() => Interlocked.CompareExchange(ref _enumerationCompletedTimestamp, Stopwatch.GetTimestamp(), 0);

		public void CaptureFinalState()
		{
			foreach (var item in _folder.Items)
			{
				_uniqueViewModels.Add(item);
			}

			UpdateMaximumRealizedRows();
		}

		public async Task WaitForInitialPresentationAsync(TimeSpan timeout)
		{
			await WaitForRealizedRowsToSettleAsync(timeout);
			var targetCount = Math.Min(_folder.Items.Count, Math.Max(1, _tableDiagnostics.RealizedRowCount));
			var targets = _folder.Items.Take(targetCount).ToArray();
			var firstPageTargets = targets.Take(FirstPageItemCount).ToArray();
			_initialPresentationItemCount = targets.Length;
			var expectsProperties = HasRequestedProperties(_session.ViewSettings);
			var started = Stopwatch.GetTimestamp();
			while (true)
			{
				foreach (var target in targets)
				{
					RecordExistingPresentation(target);
				}

				var propertiesCompleted = !expectsProperties || targets.All(item => _firstPropertyTimestamps.ContainsKey(item));
				var thumbnailsCompleted = targets.All(item => _firstThumbnailTimestamps.ContainsKey(item));
				if (!expectsProperties || firstPageTargets.All(item => _firstPropertyTimestamps.ContainsKey(item)))
				{
					var timestamp = expectsProperties ? firstPageTargets.Max(item => _firstPropertyTimestamps[item]) : _enumerationCompletedTimestamp;
					Interlocked.CompareExchange(ref _firstPagePropertiesCompletedTimestamp, timestamp, 0);
				}

				if (firstPageTargets.All(item => _firstThumbnailTimestamps.ContainsKey(item)))
				{
					Interlocked.CompareExchange(ref _firstPageThumbnailsCompletedTimestamp, firstPageTargets.Max(item => _firstThumbnailTimestamps[item]), 0);
				}

				if (propertiesCompleted)
				{
					var propertiesCompletedTimestamp = expectsProperties ? targets.Max(item => _firstPropertyTimestamps[item]) : _enumerationCompletedTimestamp;
					Interlocked.CompareExchange(ref _initialPropertiesCompletedTimestamp, propertiesCompletedTimestamp, 0);
				}

				if (thumbnailsCompleted)
				{
					Interlocked.CompareExchange(ref _initialThumbnailsCompletedTimestamp, targets.Max(item => _firstThumbnailTimestamps[item]), 0);
				}

				if (propertiesCompleted && thumbnailsCompleted)
				{
					var presentationCompletedTimestamp = Math.Max(_initialPropertiesCompletedTimestamp, _initialThumbnailsCompletedTimestamp);
					Interlocked.CompareExchange(ref _initialPresentationCompletedTimestamp, presentationCompletedTimestamp, 0);

					return;
				}

				if (Stopwatch.GetElapsedTime(started) >= timeout)
				{
					var propertyCount = targets.Count(static item => item.Properties.Count is not 0);
					var thumbnailCount = targets.Count(static item => item.Thumbnail is not null);
					Assert.Fail($"Initial presentation timed out: target={targets.Length}, properties={propertyCount}, thumbnails={thumbnailCount}.");
				}

				await WaitForUiIdleAsync(timeout);
			}
		}

		public BrowsePerformanceResult CreateResult(
			string scenario,
			DispatcherLatencyProbe dispatcherProbe,
			MeasuringUiDispatcher dispatcher,
			string? path,
			string cacheState,
			bool propertiesEnabled,
			bool thumbnailsEnabled,
			string? environmentNotes,
			int? dispatcherCallbackCountOverride = null)
		{
			return new BrowsePerformanceResult
			{
				Scenario = scenario,
				Path = path,
				ItemCount = _folder.Items.Count,
				TimeToFirstCoreBatchMs = ElapsedMilliseconds(_firstCoreBatchTimestamp),
				TimeToFirstPresentedItemMs = ElapsedMilliseconds(_firstPresentedItemTimestamp),
				TimeToFirstRealizedRowMs = ElapsedMilliseconds(_firstRealizedRowTimestamp),
				EnumerationCompleteMs = ElapsedMilliseconds(_enumerationCompletedTimestamp),
				TimeToFirstPropertiesMs = ElapsedMilliseconds(_firstPropertiesTimestamp),
				TimeToFirstThumbnailMs = ElapsedMilliseconds(_firstThumbnailTimestamp),
				FirstPagePropertiesCompleteMs = ElapsedMilliseconds(_firstPagePropertiesCompletedTimestamp),
				FirstPageThumbnailsCompleteMs = ElapsedMilliseconds(_firstPageThumbnailsCompletedTimestamp),
				InitialPropertiesCompleteMs = ElapsedMilliseconds(_initialPropertiesCompletedTimestamp),
				InitialThumbnailsCompleteMs = ElapsedMilliseconds(_initialThumbnailsCompletedTimestamp),
				InitialPresentationCompleteMs = ElapsedMilliseconds(_initialPresentationCompletedTimestamp),
				InitialPresentationItemCount = _initialPresentationItemCount,
				ThumbnailUpdateCount = _thumbnailUpdateCounts.Values.Sum(),
				RepeatedThumbnailUpdateCount = RepeatedThumbnailUpdateCount,
				FallbackThumbnailPublicationCount = Volatile.Read(ref _fallbackThumbnailPublicationCount),
				RepeatedFallbackThumbnailPublicationCount = RepeatedFallbackThumbnailPublicationCount,
				ContentThumbnailPublicationCount = ContentThumbnailPublicationCount,
				MaximumUiLatencyMs = dispatcherProbe.MaximumLatency.TotalMilliseconds,
				P95UiLatencyMs = dispatcherProbe.P95Latency.TotalMilliseconds,
				StallsOver16Ms = dispatcherProbe.CountOver(TimeSpan.FromMilliseconds(16)),
				StallsOver50Ms = dispatcherProbe.CountOver(TimeSpan.FromMilliseconds(50)),
				StallsOver100Ms = dispatcherProbe.CountOver(TimeSpan.FromMilliseconds(100)),
				DispatcherProbeCount = dispatcherProbe.SampleCount,
				DispatcherCallbackCount = dispatcherCallbackCountOverride ?? dispatcher.EnqueueCount,
				CollectionNotificationCount = CollectionNotificationCount,
				UniqueViewModelCount = UniqueViewModelCount,
				MaximumRealizedRowCount = MaximumRealizedRowCount,
				CacheState = cacheState,
				PropertiesEnabled = propertiesEnabled,
				ThumbnailsEnabled = thumbnailsEnabled,
				Environment = BrowseEnvironmentMetadata.Create(environmentNotes),
			};
		}

		public void Dispose()
		{
			_session.ItemsChanged -= Session_ItemsChanged;
			_session.ItemPresentationChanged -= Session_ItemPresentationChanged;
			_folder.Items.CollectionChanged -= Items_CollectionChanged;
			_tableDiagnostics.RowRealized -= TableDiagnostics_RowRealized;
			_tableDiagnostics.RealizedRowCountChanged -= TableDiagnostics_RealizedRowCountChanged;
			foreach (var item in _observedItems)
			{
				item.PropertyChanged -= Item_PropertyChanged;
			}
		}

		private void Session_ItemsChanged(object? sender, BrowseItemsChangedEventArgs e)
		{
			if (!_started || _session.Items.Count is 0)
			{
				return;
			}

			if (Interlocked.CompareExchange(ref _firstCoreBatchTimestamp, Stopwatch.GetTimestamp(), 0) is 0)
			{
				_firstCoreBatch.TrySetResult(true);
			}
		}

		private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			if (!_started)
			{
				return;
			}

			Interlocked.Increment(ref _collectionNotificationCount);
			if (e.NewItems is not null)
			{
				foreach (var item in e.NewItems.OfType<BrowseItemViewModel>())
				{
					_uniqueViewModels.Add(item);
					ObserveItem(item);
				}
			}

			if (_folder.Items.Count is not 0 && Interlocked.CompareExchange(ref _firstPresentedItemTimestamp, Stopwatch.GetTimestamp(), 0) is 0)
			{
				_firstPresentedItem.TrySetResult(true);
			}
		}

		private void Session_ItemPresentationChanged(object? sender, BrowseItemPresentationChangedEventArgs e)
		{
			if (!_started || _session.Generation == _baselineGeneration || (e.Changed & BrowseItemPresentationChangeFlags.Thumbnail) is 0 || e.Presentation.Thumbnail is not { } thumbnail)
			{
				return;
			}

			if (thumbnail.IsFallback)
			{
				Interlocked.Increment(ref _fallbackThumbnailPublicationCount);
				_fallbackThumbnailPublicationCounts.AddOrUpdate(e.Key, 1, static (_, count) => count + 1);
			}
			else
			{
				Interlocked.Increment(ref _contentThumbnailPublicationCount);
			}
		}

		private void TableDiagnostics_RowRealized(object? sender, TableViewRowChangingEventArgs e)
		{
			if (!_started)
			{
				return;
			}

			UpdateMaximumRealizedRows();
			if (Interlocked.CompareExchange(ref _firstRealizedRowTimestamp, Stopwatch.GetTimestamp(), 0) is 0)
			{
				_firstRealizedRow.TrySetResult(true);
			}
		}

		private void TableDiagnostics_RealizedRowCountChanged(object? sender, EventArgs e) => UpdateMaximumRealizedRows();

		private void ObserveItem(BrowseItemViewModel item)
		{
			if (_observedItems.Add(item))
			{
				item.PropertyChanged += Item_PropertyChanged;
				RecordExistingPresentation(item);
			}
		}

		private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (!_started || _session.Generation == _baselineGeneration || sender is not BrowseItemViewModel item || !_session.Contains(item.Reference.GetKey()))
			{
				return;
			}

			if (e.PropertyName is nameof(BrowseItemViewModel.Properties) && item.Properties.Count is not 0)
			{
				RecordProperties(item, Stopwatch.GetTimestamp());
			}
			else if (e.PropertyName is nameof(BrowseItemViewModel.Thumbnail) && item.Thumbnail is not null)
			{
				RecordThumbnail(item, Stopwatch.GetTimestamp());
				_thumbnailUpdateCounts[item] = _thumbnailUpdateCounts.GetValueOrDefault(item) + 1;
			}
		}

		private void RecordExistingPresentation(BrowseItemViewModel item)
		{
			if (_session.Generation == _baselineGeneration || !_session.Contains(item.Reference.GetKey()))
			{
				return;
			}

			var timestamp = Stopwatch.GetTimestamp();
			if (item.Properties.Count is not 0)
			{
				RecordProperties(item, timestamp);
			}

			if (item.Thumbnail is not null)
			{
				RecordThumbnail(item, timestamp);
				_thumbnailUpdateCounts.TryAdd(item, 1);
			}
		}

		private void RecordProperties(BrowseItemViewModel item, long timestamp)
		{
			if (_firstPropertyTimestamps.TryAdd(item, timestamp))
			{
				Interlocked.CompareExchange(ref _firstPropertiesTimestamp, timestamp, 0);
			}
		}

		private void RecordThumbnail(BrowseItemViewModel item, long timestamp)
		{
			if (_firstThumbnailTimestamps.TryAdd(item, timestamp))
			{
				Interlocked.CompareExchange(ref _firstThumbnailTimestamp, timestamp, 0);
			}
		}

		private void UpdateMaximumRealizedRows()
		{
			var candidate = _tableDiagnostics.RealizedRowCount;
			var current = Volatile.Read(ref _maximumRealizedRowCount);
			while (candidate > current)
			{
				var previous = Interlocked.CompareExchange(ref _maximumRealizedRowCount, candidate, current);
				if (previous == current)
				{
					return;
				}

				current = previous;
			}
		}

		private async Task WaitForRealizedRowsToSettleAsync(TimeSpan timeout)
		{
			var started = Stopwatch.GetTimestamp();
			var previousCount = -1;
			var stableSampleCount = 0;
			while (stableSampleCount < 2)
			{
				await WaitForUiIdleAsync(timeout);
				await Task.Delay(16);
				var currentCount = _tableDiagnostics.RealizedRowCount;
				stableSampleCount = currentCount > 0 && currentCount == previousCount ? stableSampleCount + 1 : 0;
				previousCount = currentCount;
				if (Stopwatch.GetElapsedTime(started) >= timeout)
				{
					Assert.Fail("Realized rows did not stabilize before the timeout.");
				}
			}
		}

		private double? ElapsedMilliseconds(long timestamp) => timestamp is 0 ? null : Stopwatch.GetElapsedTime(_startTimestamp, timestamp).TotalMilliseconds;

		private static bool HasRequestedProperties(BrowseViewSettings settings)
		{
			return settings.Columns.Any(static column => column.IsVisible && !column.PropertyId.Equals("name", StringComparison.OrdinalIgnoreCase)
				&& !column.PropertyId.Equals("System.ItemNameDisplay", StringComparison.Ordinal));
		}
	}

	private sealed class MeasuringUiDispatcher : IUIDispatcher
	{
		private readonly DispatcherQueue _dispatcherQueue;
		private int _enqueueCount;

		public bool HasThreadAccess => _dispatcherQueue.HasThreadAccess;
		public int EnqueueCount => Volatile.Read(ref _enqueueCount);

		public MeasuringUiDispatcher(DispatcherQueue dispatcherQueue)
		{
			_dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
		}

		public bool TryEnqueue(Action callback) => TryEnqueue(DispatcherQueuePriority.Normal, callback);

		public bool TryEnqueue(DispatcherQueuePriority priority, Action callback)
		{
			ArgumentNullException.ThrowIfNull(callback);

			Interlocked.Increment(ref _enqueueCount);

			return _dispatcherQueue.TryEnqueue(priority, () => callback());
		}
	}

	private sealed class DispatcherLatencyProbe : IAsyncDisposable
	{
		private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(16);
		private readonly DispatcherQueue _dispatcherQueue;
		private readonly ConcurrentQueue<TimeSpan> _samples = new();
		private readonly CancellationTokenSource _cancellation = new();
		private Task? _producer;
		private int _stopped;

		public int SampleCount => _samples.Count;
		public TimeSpan MaximumLatency => _samples.IsEmpty ? TimeSpan.Zero : _samples.Max();

		public TimeSpan P95Latency
		{
			get
			{
				var samples = _samples.OrderBy(static value => value).ToArray();
				if (samples.Length is 0)
				{
					return TimeSpan.Zero;
				}

				var index = Math.Clamp((int)Math.Ceiling(samples.Length * 0.95) - 1, 0, samples.Length - 1);

				return samples[index];
			}
		}

		public DispatcherLatencyProbe(DispatcherQueue dispatcherQueue)
		{
			_dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
		}

		public void Start() => _producer ??= Task.Run(ProduceAsync);
		public int CountOver(TimeSpan threshold) => _samples.Count(sample => sample > threshold);

		public async Task StopAsync()
		{
			if (Interlocked.Exchange(ref _stopped, 1) is not 0)
			{
				return;
			}

			_cancellation.Cancel();
			if (_producer is not null)
			{
				try
				{
					await _producer;
				}
				catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
				{
				}
			}

			await WaitForUiIdleAsync();
		}

		public async ValueTask DisposeAsync()
		{
			await StopAsync();
			_cancellation.Dispose();
		}

		private async Task ProduceAsync()
		{
			while (true)
			{
				_cancellation.Token.ThrowIfCancellationRequested();

				var scheduledTimestamp = Stopwatch.GetTimestamp();
				if (!_dispatcherQueue.TryEnqueue(() => _samples.Enqueue(Stopwatch.GetElapsedTime(scheduledTimestamp))))
				{
					return;
				}

				await Task.Delay(SampleInterval, _cancellation.Token).ConfigureAwait(false);
			}
		}
	}

	private sealed class PerformanceWindowHost : IDisposable
	{
		private readonly Window _window;
		private readonly FrameworkElement _content;
		private bool _isClosed;

		private PerformanceWindowHost(Window window, FrameworkElement content)
		{
			_window = window;
			_content = content;
		}

		public static async Task<PerformanceWindowHost> ShowAsync(FrameworkElement content)
		{
			ArgumentNullException.ThrowIfNull(content);

			var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			void Content_Loaded(object sender, RoutedEventArgs e) => loaded.TrySetResult(true);
			content.Loaded += Content_Loaded;
			var window = new Window { Content = content };
			window.Activate();
			try
			{
				if (!content.IsLoaded)
				{
					await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
				}
			}
			finally
			{
				content.Loaded -= Content_Loaded;
			}

			await WaitForUiIdleAsync();

			return new PerformanceWindowHost(window, content);
		}

		public async Task CloseAsync()
		{
			if (_isClosed)
			{
				return;
			}

			_isClosed = true;
			var unloaded = WaitForUnloadedAsync(_content);
			_window.Content = null;
			await unloaded;
			_window.Close();
			await WaitForUiIdleAsync(StressUiTimeout);
		}

		public void Dispose()
		{
			if (_isClosed)
			{
				return;
			}

			_isClosed = true;
			_window.Content = null;
			_window.Close();
		}
	}

	private sealed class NoOpCommandHandler(CommandId id) : ICommandHandler
	{
		public CommandId Id { get; } = id;
		public CommandConcurrencyPolicy ConcurrencyPolicy => CommandConcurrencyPolicy.AllowParallel;
		public CommandStateInvalidation StateDependencies => CommandStateInvalidation.None;
		public CommandState GetState(CommandContext context) => new(IsVisible: true, IsEnabled: true);
		public ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default) => ValueTask.FromResult(CommandExecutionResult.Succeeded());
	}

	private sealed class NoOpStorageOperationService : IStorageOperationService
	{
		public bool CanHandle(StorageOperationRequest request) => false;
		public ValueTask<StorageOperationResult> ExecuteAsync(StorageOperationRequest request, IProgress<StorageOperationProgress>? progress = null, CancellationToken cancellationToken = default,
			IStorageOperationControl? operationControl = null) => throw new NotSupportedException();
	}

	private sealed class PerformanceBrowseLocationResolver(
		int itemCount,
		Func<int, CancellationToken, ValueTask> beforeYieldAsync,
		Func<int, PropertyRequest, CancellationToken, ValueTask<IReadOnlyDictionary<string, object?>>>? getPropertiesAsync) : IBrowseLocationResolver
	{
		private readonly PerformanceStorageSource _source = new();

		public ValueTask<IBrowseLocationContext> OpenAsync(BrowseLocation location, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(location);

			cancellationToken.ThrowIfCancellationRequested();

			return ValueTask.FromResult<IBrowseLocationContext>(new PerformanceBrowseLocationContext(location, itemCount, _source, beforeYieldAsync, getPropertiesAsync));
		}
	}

	private sealed class PerformanceBrowseLocationContext(
		BrowseLocation location,
		int itemCount,
		PerformanceStorageSource source,
		Func<int, CancellationToken, ValueTask> beforeYieldAsync,
		Func<int, PropertyRequest, CancellationToken, ValueTask<IReadOnlyDictionary<string, object?>>>? getPropertiesAsync) : IBrowseLocationContext
	{
		public BrowseLocation Location { get; } = location;
		public IStorableModel? LocationModel => null;

		public async IAsyncEnumerable<IStorableModel> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			for (var index = 0; index < itemCount; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				await beforeYieldAsync(index, cancellationToken);
				var storable = new PerformanceStorable($"item-{index:D5}", $"Item {index:D5}", index, getPropertiesAsync);
				var reference = new StorableReference(source.SourceId, storable.Id, new StorageAddress("performance", storable.Id));
				var context = new ItemContext(source, storable, reference);

				yield return new StorableModel(storable, reference, CapabilityRegistry.Empty.CreateCapabilities(context));
			}

			await Task.CompletedTask;
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

	private sealed class PerformanceStorageWorkspace : IStorageWorkspace
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

	private sealed class PerformanceStorageSource : IStorageSource
	{
		public StorageSourceId SourceId { get; } = new("performance");
		public string SourceType => "performance";
		public string DisplayName => "Performance";

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

	private sealed class PerformanceStorable(
		string id,
		string name,
		int index,
		Func<int, PropertyRequest, CancellationToken, ValueTask<IReadOnlyDictionary<string, object?>>>? getPropertiesAsync) : IStorable, IPropertySource
	{
		public string Id { get; } = id;
		public string Name { get; } = name;

		public ValueTask<IReadOnlyDictionary<string, object?>> GetPropertiesAsync(PropertyRequest request, CancellationToken cancellationToken = default) =>
			getPropertiesAsync is null
				? ValueTask.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>())
				: getPropertiesAsync(index, request, cancellationToken);
	}

	private sealed class BrowsePerformanceResult
	{
		public required string Scenario { get; init; }
		public string? Path { get; init; }
		public int ItemCount { get; init; }
		public double? TimeToFirstCoreBatchMs { get; init; }
		public double? TimeToFirstPresentedItemMs { get; init; }
		public double? TimeToFirstRealizedRowMs { get; init; }
		public double? EnumerationCompleteMs { get; init; }
		public double? TimeToFirstPropertiesMs { get; init; }
		public double? TimeToFirstThumbnailMs { get; init; }
		public double? FirstPagePropertiesCompleteMs { get; init; }
		public double? FirstPageThumbnailsCompleteMs { get; init; }
		public double? InitialPropertiesCompleteMs { get; init; }
		public double? InitialThumbnailsCompleteMs { get; init; }
		public double? InitialPresentationCompleteMs { get; init; }
		public int InitialPresentationItemCount { get; init; }
		public int ThumbnailUpdateCount { get; init; }
		public int RepeatedThumbnailUpdateCount { get; init; }
		public int FallbackThumbnailPublicationCount { get; init; }
		public int RepeatedFallbackThumbnailPublicationCount { get; init; }
		public int ContentThumbnailPublicationCount { get; init; }
		public double MaximumUiLatencyMs { get; init; }
		public double P95UiLatencyMs { get; init; }
		public int StallsOver16Ms { get; init; }
		public int StallsOver50Ms { get; init; }
		public int StallsOver100Ms { get; init; }
		public int DispatcherProbeCount { get; init; }
		public int DispatcherCallbackCount { get; init; }
		public int CollectionNotificationCount { get; init; }
		public int UniqueViewModelCount { get; init; }
		public int MaximumRealizedRowCount { get; init; }
		public required string CacheState { get; init; }
		public bool PropertiesEnabled { get; init; }
		public bool ThumbnailsEnabled { get; init; }
		public required BrowseEnvironmentMetadata Environment { get; init; }
	}

	private sealed class BrowseEnvironmentMetadata
	{
		public required string OsVersion { get; init; }
		public required string ProcessArchitecture { get; init; }
		public string? Processor { get; init; }
		public long AvailableMemoryBytes { get; init; }
		public string? Notes { get; init; }

		public static BrowseEnvironmentMetadata Create(string? notes) => new()
		{
			OsVersion = Environment.OSVersion.VersionString,
			ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
			Processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
			AvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
			Notes = notes,
		};
	}

	private static class BrowsePerformanceResultWriter
	{
		public static async Task<string> WriteAsync(string name, IReadOnlyList<BrowsePerformanceResult> results)
		{
			var outputDirectory = Environment.GetEnvironmentVariable("FILES_PERF_RESULTS_DIR");
			if (string.IsNullOrWhiteSpace(outputDirectory))
			{
				outputDirectory = Path.Combine(Path.GetTempPath(), "ReFiles", "PerformanceResults");
			}

			Directory.CreateDirectory(outputDirectory);
			var path = Path.Combine(outputDirectory, $"{name}.json");
			var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
			await File.WriteAllTextAsync(path, json);

			return path;
		}
	}
}

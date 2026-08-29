// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;

namespace Files.UITests.Views;

/// <summary>
/// Verifies rectangle-selection target coordination in a live WinUI visual tree.
/// </summary>
[TestClass]
public sealed class RectangleSelectionHostTests
{
	private const int DefaultStressIterationCount = 25;
	private const int MaximumStressIterationCount = 1_000;

	/// <summary>
	/// Verifies that attached targets register with and unregister from their nearest host.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task AttachedTargetsFollowHostLifetime()
	{
		var listView = new ListView();
		var gridView = new GridView();
		var tableView = new TableView();
		RectangleSelection.SetIsTarget(listView, true);
		RectangleSelection.SetIsTarget(gridView, true);
		RectangleSelection.SetIsTarget(tableView, true);
		var content = new StackPanel();
		content.Children.Add(listView);
		content.Children.Add(gridView);
		content.Children.Add(tableView);
		var host = new RectangleSelectionHost { Content = content };
		var window = new Window { Content = host };
		try
		{
			var loaded = WaitForLoadedAsync(host);
			window.Activate();
			await loaded;
			await WaitForDispatcherAsync();
			Assert.AreEqual(3, host.TargetCount);

			var unloaded = WaitForUnloadedAsync(gridView);
			content.Children.Remove(gridView);
			await unloaded;
			Assert.AreEqual(2, host.TargetCount);
		}
		finally
		{
			window.Close();
		}
	}

	/// <summary>
	/// Verifies that batched selection state and completion notifications stay scoped to their target.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task SelectionUpdateNotificationsAreTargetScoped()
	{
		var firstTarget = new ListView();
		var secondTarget = new GridView();
		var firstUpdateCount = 0;
		var secondUpdateCount = 0;
		EventHandler firstHandler = (_, _) => firstUpdateCount++;
		EventHandler secondHandler = (_, _) => secondUpdateCount++;
		RectangleSelection.AddSelectionUpdatedHandler(firstTarget, firstHandler);
		RectangleSelection.AddSelectionUpdatedHandler(secondTarget, secondHandler);
		try
		{
			RectangleSelection.BeginSelectionUpdate([firstTarget, secondTarget]);
			Assert.IsTrue(RectangleSelection.GetIsUpdatingSelection(firstTarget));
			Assert.IsTrue(RectangleSelection.GetIsUpdatingSelection(secondTarget));

			RectangleSelection.EndSelectionUpdate([firstTarget, secondTarget]);
			RectangleSelection.RaiseSelectionUpdated([firstTarget]);
			Assert.IsFalse(RectangleSelection.GetIsUpdatingSelection(firstTarget));
			Assert.IsFalse(RectangleSelection.GetIsUpdatingSelection(secondTarget));
			Assert.AreEqual(1, firstUpdateCount);
			Assert.AreEqual(0, secondUpdateCount);
		}
		finally
		{
			RectangleSelection.RemoveSelectionUpdatedHandler(firstTarget, firstHandler);
			RectangleSelection.RemoveSelectionUpdatedHandler(secondTarget, secondHandler);
		}

		await WaitForDispatcherAsync();
	}

	/// <summary>
	/// Verifies that gesture notifications are raised for the first selected item and the final selection.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task GestureNotificationsAreDeferredUntilFirstSelectionAndCompletion()
	{
		var target = new ListView { SelectionMode = ListViewSelectionMode.Extended };
		target.Items.Add("one");
		target.Items.Add("two");
		var model = new RectangleSelectionNotificationModel();

		Assert.AreEqual(0, model.RecordChanges([target]).Count);
		target.SelectedItems.Add("one");
		CollectionAssert.AreEqual(new[] { target }, model.RecordChanges([target]).ToArray());

		target.SelectedItems.Add("two");
		Assert.AreEqual(0, model.RecordChanges([target]).Count);
		CollectionAssert.AreEqual(new[] { target }, model.Complete().ToArray());
		Assert.AreEqual(0, model.Complete().Count);

		target.SelectedItems.Clear();
		Assert.AreEqual(0, model.RecordChanges([target]).Count);
		CollectionAssert.AreEqual(new[] { target }, model.Complete().ToArray());

		await WaitForDispatcherAsync();
	}

	/// <summary>
	/// Verifies that repeated target registration, selection updates, and window teardown release every target.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	[TestCategory("Stress")]
	public async Task RepeatedTargetLifetimesReleaseSelectionState()
	{
		var iterationCount = ReadStressIterationCount();
		for (var iteration = 0; iteration < iterationCount; iteration++)
		{
			var items = Enumerable.Range(0, 32).Select(static index => $"Item {index}").ToArray();
			var listView = new ListView { ItemsSource = items, SelectionMode = ListViewSelectionMode.Extended };
			var gridView = new GridView { ItemsSource = items, SelectionMode = ListViewSelectionMode.Extended };
			var tableView = new TableView { ItemsSource = items };
			RectangleSelection.SetIsTarget(listView, true);
			RectangleSelection.SetIsTarget(gridView, true);
			RectangleSelection.SetIsTarget(tableView, true);
			var content = new StackPanel();
			content.Children.Add(listView);
			content.Children.Add(gridView);
			content.Children.Add(tableView);
			var host = new RectangleSelectionHost { Content = content };
			var window = new Window { Content = host };
			try
			{
				var loaded = WaitForLoadedAsync(host);
				window.Activate();
				await loaded;
				await WaitForDispatcherAsync();
				Assert.AreEqual(3, host.TargetCount, $"Target registration mismatch at iteration {iteration}.");

				var tableListView = (ListViewBase)tableView.RowsHost!.Element;
				var targets = new[] { listView, gridView, tableListView };
				RectangleSelection.BeginSelectionUpdate(targets);
				foreach (var target in targets)
				{
					target.SelectedItems.Add(items[iteration % items.Length]);
					target.SelectedItems.Add(items[(iteration + 7) % items.Length]);
				}

				RectangleSelection.EndSelectionUpdate(targets);
				RectangleSelection.RaiseSelectionUpdated(targets);
				var unloaded = Task.WhenAll(WaitForUnloadedAsync(host), WaitForUnloadedAsync(listView), WaitForUnloadedAsync(gridView), WaitForUnloadedAsync(tableView));
				window.Content = null;
				await unloaded;
				await WaitForDispatcherAsync();
				Assert.AreEqual(0, host.TargetCount, $"Selection targets leaked at iteration {iteration}.");
			}
			finally
			{
				window.Close();
			}
		}
	}

	private static async Task WaitForDispatcherAsync()
	{
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Assert.IsTrue(UnitTestApp.TestDispatcherQueue.TryEnqueue(completion.SetResult));
		await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}

	private static Task WaitForLoadedAsync(FrameworkElement element)
	{
		if (element.IsLoaded)
		{
			return Task.CompletedTask;
		}

		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		RoutedEventHandler? handler = null;
		handler = (_, _) =>
		{
			element.Loaded -= handler;
			completion.SetResult();
		};
		element.Loaded += handler;

		return completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}

	private static Task WaitForUnloadedAsync(FrameworkElement element)
	{
		if (!element.IsLoaded)
		{
			return Task.CompletedTask;
		}

		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		RoutedEventHandler? handler = null;
		handler = (_, _) =>
		{
			element.Unloaded -= handler;
			completion.SetResult();
		};
		element.Unloaded += handler;

		return completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}

	private static int ReadStressIterationCount()
	{
		var value = Environment.GetEnvironmentVariable("FILES_UI_STRESS_ITERATIONS");
		if (string.IsNullOrWhiteSpace(value))
		{
			return DefaultStressIterationCount;
		}

		if (!int.TryParse(value, out var iterationCount) || iterationCount < 1 || iterationCount > MaximumStressIterationCount)
		{
			throw new InvalidOperationException($"FILES_UI_STRESS_ITERATIONS must be between 1 and {MaximumStressIterationCount}.");
		}

		return iterationCount;
	}
}

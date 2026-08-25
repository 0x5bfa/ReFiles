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

	private static async Task WaitForDispatcherAsync()
	{
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Assert.IsTrue(App.TestDispatcherQueue.TryEnqueue(completion.SetResult));
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
}

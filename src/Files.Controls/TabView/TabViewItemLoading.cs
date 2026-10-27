// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Files.Controls;

/// <summary>
/// Connects a loading state to the loading visual state in the rounded tab template.
/// </summary>
public static class TabViewItemLoading
{
	private const int LoadingIndicatorDelayMilliseconds = 150;

	private static readonly ConditionalWeakTable<TabViewItem, LoadingIndicatorState> _loadingIndicatorStates = [];

	/// <summary>Identifies the attached loading state property.</summary>
	public static readonly DependencyProperty IsLoadingProperty =
		DependencyProperty.RegisterAttached("IsLoading", typeof(bool), typeof(TabViewItemLoading), new PropertyMetadata(false, IsLoadingChanged));

	/// <summary>Gets whether the tab's content is loading.</summary>
	/// <param name="element">The tab item.</param>
	/// <returns><see langword="true"/> when the tab's content is loading.</returns>
	public static bool GetIsLoading(DependencyObject element)
	{
		ArgumentNullException.ThrowIfNull(element);

		return (bool)element.GetValue(IsLoadingProperty);
	}

	/// <summary>Sets whether the tab's content is loading.</summary>
	/// <param name="element">The tab item.</param>
	/// <param name="value">Whether the tab's content is loading.</param>
	public static void SetIsLoading(DependencyObject element, bool value)
	{
		ArgumentNullException.ThrowIfNull(element);

		element.SetValue(IsLoadingProperty, value);
	}

	private static void IsLoadingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
	{
		if (sender is not TabViewItem tabItem)
		{
			return;
		}

		tabItem.Loaded -= TabItem_Loaded;
		tabItem.Loaded += TabItem_Loaded;
		tabItem.Unloaded -= TabItem_Unloaded;
		tabItem.Unloaded += TabItem_Unloaded;

		var state = GetLoadingIndicatorState(tabItem);

		if (GetIsLoading(tabItem))
		{
			state.IsVisible = false;
			StartLoadingIndicatorTimer(tabItem, state);
		}
		else
		{
			StopLoadingIndicatorTimer(state);
			state.IsVisible = false;
		}

		UpdateVisualState(tabItem, state);
	}

	private static void TabItem_Loaded(object sender, RoutedEventArgs args)
	{
		if (sender is TabViewItem tabItem)
		{
			var state = GetLoadingIndicatorState(tabItem);

			if (GetIsLoading(tabItem) && !state.IsVisible && state.Timer is null)
			{
				StartLoadingIndicatorTimer(tabItem, state);
			}

			UpdateVisualState(tabItem, state);
		}
	}

	private static void TabItem_Unloaded(object sender, RoutedEventArgs args)
	{
		if (sender is TabViewItem tabItem)
		{
			var state = GetLoadingIndicatorState(tabItem);
			StopLoadingIndicatorTimer(state);
			state.IsVisible = false;
		}
	}

	private static LoadingIndicatorState GetLoadingIndicatorState(TabViewItem tabItem)
	{
		return _loadingIndicatorStates.GetValue(tabItem, static _ => new LoadingIndicatorState());
	}

	private static void StartLoadingIndicatorTimer(TabViewItem tabItem, LoadingIndicatorState state)
	{
		StopLoadingIndicatorTimer(state);

		var timer = new LoadingIndicatorTimer(tabItem, state);
		state.Timer = timer;
		timer.Start();
	}

	private static void StopLoadingIndicatorTimer(LoadingIndicatorState state)
	{
		if (state.Timer is LoadingIndicatorTimer timer)
		{
			timer.Stop();
			state.Timer = null;
		}
	}

	private static void UpdateVisualState(TabViewItem tabItem, LoadingIndicatorState state)
	{
		VisualStateManager.GoToState(tabItem, state.IsVisible ? "Loading" : "NotLoading", true);
	}

	private sealed class LoadingIndicatorState
	{
		public LoadingIndicatorTimer? Timer { get; set; }
		public bool IsVisible { get; set; }
	}

	private sealed class LoadingIndicatorTimer
	{
		private readonly TabViewItem _tabItem;
		private readonly LoadingIndicatorState _state;
		private readonly DispatcherQueueTimer _timer;

		public LoadingIndicatorTimer(TabViewItem tabItem, LoadingIndicatorState state)
		{
			_tabItem = tabItem;
			_state = state;
			_timer = tabItem.DispatcherQueue.CreateTimer();
			_timer.Interval = TimeSpan.FromMilliseconds(LoadingIndicatorDelayMilliseconds);
			_timer.IsRepeating = false;
			_timer.Tick += Timer_Tick;
		}

		public void Start()
		{
			_timer.Start();
		}

		public void Stop()
		{
			_timer.Stop();
			_timer.Tick -= Timer_Tick;
		}

		private void Timer_Tick(DispatcherQueueTimer sender, object args)
		{
			Stop();

			if (!ReferenceEquals(_state.Timer, this))
			{
				return;
			}

			_state.Timer = null;

			if (GetIsLoading(_tabItem))
			{
				_state.IsVisible = true;
				UpdateVisualState(_tabItem, _state);
			}
		}
	}
}

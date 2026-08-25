// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Media;
using System.Runtime.CompilerServices;

namespace Files.Controls;

/// <summary>
/// Registers selectable controls with their nearest <see cref="RectangleSelectionHost"/>.
/// </summary>
public static class RectangleSelection
{
	private static readonly ConditionalWeakTable<FrameworkElement, RectangleSelectionHost> _registeredHosts = new();
	private static readonly ConditionalWeakTable<ListViewBase, SelectionUpdateState> _selectionUpdateStates = new();

	/// <summary>Identifies the attached target property.</summary>
	public static readonly DependencyProperty IsTargetProperty = DependencyProperty.RegisterAttached("IsTarget", typeof(bool), typeof(RectangleSelection), new PropertyMetadata(false, IsTargetChanged));

	/// <summary>Gets whether an element participates in rectangle selection.</summary>
	/// <param name="element">The potential selection target.</param>
	/// <returns><see langword="true"/> when the element is registered as a target.</returns>
	public static bool GetIsTarget(DependencyObject element)
	{
		ArgumentNullException.ThrowIfNull(element);

		return (bool)element.GetValue(IsTargetProperty);
	}

	/// <summary>Gets whether rectangle selection is currently updating a target.</summary>
	/// <param name="target">The registered list control.</param>
	/// <returns><see langword="true"/> while the target selection is being updated.</returns>
	public static bool GetIsUpdatingSelection(ListViewBase target)
	{
		ArgumentNullException.ThrowIfNull(target);

		return _selectionUpdateStates.TryGetValue(target, out var state) && state.IsUpdating;
	}

	/// <summary>Sets whether an element participates in rectangle selection.</summary>
	/// <param name="element">The potential selection target.</param>
	/// <param name="value">Whether the element should be registered as a target.</param>
	public static void SetIsTarget(DependencyObject element, bool value)
	{
		ArgumentNullException.ThrowIfNull(element);

		element.SetValue(IsTargetProperty, value);
	}

	/// <summary>Adds a handler that runs after the first item is selected and when the rectangle-selection gesture completes.</summary>
	/// <param name="target">The registered list control.</param>
	/// <param name="handler">The handler to add.</param>
	public static void AddSelectionUpdatedHandler(ListViewBase target, EventHandler handler)
	{
		ArgumentNullException.ThrowIfNull(target);

		ArgumentNullException.ThrowIfNull(handler);

		_selectionUpdateStates.GetOrCreateValue(target).SelectionUpdated += handler;
	}

	/// <summary>Removes a handler that runs after the first item is selected and when the rectangle-selection gesture completes.</summary>
	/// <param name="target">The registered list control.</param>
	/// <param name="handler">The handler to remove.</param>
	public static void RemoveSelectionUpdatedHandler(ListViewBase target, EventHandler handler)
	{
		ArgumentNullException.ThrowIfNull(target);

		ArgumentNullException.ThrowIfNull(handler);

		if (_selectionUpdateStates.TryGetValue(target, out var state))
		{
			state.SelectionUpdated -= handler;
		}
	}

	internal static void BeginSelectionUpdate(IEnumerable<ListViewBase> targets)
	{
		foreach (var target in targets.Distinct())
		{
			_selectionUpdateStates.GetOrCreateValue(target).IsUpdating = true;
		}
	}

	internal static void EndSelectionUpdate(IEnumerable<ListViewBase> targets)
	{
		foreach (var target in targets.Distinct())
		{
			_selectionUpdateStates.GetOrCreateValue(target).IsUpdating = false;
		}
	}

	internal static void RaiseSelectionUpdated(IEnumerable<ListViewBase> targets)
	{
		foreach (var target in targets.Distinct())
		{
			if (_selectionUpdateStates.TryGetValue(target, out var state))
			{
				state.RaiseSelectionUpdated(target);
			}
		}
	}

	private static RectangleSelectionHost? FindHost(FrameworkElement target)
	{
		for (var current = VisualTreeHelper.GetParent(target); current is not null; current = VisualTreeHelper.GetParent(current))
		{
			if (current is RectangleSelectionHost host)
			{
				return host;
			}
		}

		return null;
	}

	private static void IsTargetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
	{
		if (sender is not FrameworkElement target)
		{
			return;
		}

		target.Loaded -= Target_Loaded;
		target.Unloaded -= Target_Unloaded;
		UnregisterTarget(target);
		if (args.NewValue is true)
		{
			target.Loaded += Target_Loaded;
			target.Unloaded += Target_Unloaded;
			if (target.IsLoaded)
			{
				RegisterTarget(target);
			}
		}
	}

	private static void RegisterTarget(FrameworkElement target)
	{
		var host = FindHost(target);
		if (host is null)
		{
			return;
		}

		host.RegisterTarget(target);
		_registeredHosts.Remove(target);
		_registeredHosts.Add(target, host);
	}

	private static void Target_Loaded(object sender, RoutedEventArgs args)
	{
		if (sender is FrameworkElement target)
		{
			RegisterTarget(target);
		}
	}

	private static void Target_Unloaded(object sender, RoutedEventArgs args)
	{
		if (sender is FrameworkElement target)
		{
			UnregisterTarget(target);
		}
	}

	private static void UnregisterTarget(FrameworkElement target)
	{
		if (_registeredHosts.TryGetValue(target, out var host))
		{
			host.UnregisterTarget(target);
			_registeredHosts.Remove(target);
		}
	}

	private sealed class SelectionUpdateState
	{
		internal bool IsUpdating { get; set; }

		internal event EventHandler? SelectionUpdated;

		internal void RaiseSelectionUpdated(ListViewBase target)
		{
			SelectionUpdated?.Invoke(target, EventArgs.Empty);
		}
	}
}

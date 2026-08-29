// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.WinUI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Files.Controls;

/// <summary>
/// Hosts one rectangle-selection scope containing one or more attached targets.
/// </summary>
[TemplatePart(Name = SelectionLayerPartName, Type = typeof(Canvas))]
[TemplatePart(Name = SelectionRectanglePartName, Type = typeof(Rectangle))]
public sealed partial class RectangleSelectionHost : ContentControl
{
	private const double AutoScrollEdgeSize = 48;
	private const int AutoScrollIntervalMilliseconds = 16;
	private const double DragThreshold = 5;
	private const double MaximumAutoScrollDelta = 24;
	private const string SelectionLayerPartName = "PART_SelectionLayer";
	private const string SelectionRectanglePartName = "PART_SelectionRectangle";

	private readonly DispatcherQueueTimer _autoScrollTimer;
	private readonly PointerEventHandler _pointerCanceledHandler;
	private readonly PointerEventHandler _pointerCaptureLostHandler;
	private readonly PointerEventHandler _pointerMovedHandler;
	private readonly PointerEventHandler _pointerPressedHandler;
	private readonly PointerEventHandler _pointerReleasedHandler;
	private readonly HashSet<FrameworkElement> _registeredSources = [];
	private readonly Dictionary<FrameworkElement, TargetState> _targets = [];
	private readonly RectangleSelectionNotificationModel _notificationModel = new();
	private TargetState? _activeTarget;
	private Point _autoScrollDelta;
	private Pointer? _capturedPointer;
	private Point _originContentPoint;
	private Point _pointerPosition;
	private Canvas? _selectionLayer;
	private Rectangle? _selectionRectangle;
	private ScrollViewer? _scrollOwner;
	private SelectionState _state;

	internal int TargetCount => _targets.Count;

	/// <summary>
	/// Initializes a rectangle-selection host.
	/// </summary>
	public RectangleSelectionHost()
	{
		DefaultStyleKey = typeof(RectangleSelectionHost);
		_pointerCanceledHandler = Host_PointerCanceled;
		_pointerCaptureLostHandler = Host_PointerCaptureLost;
		_pointerMovedHandler = Host_PointerMoved;
		_pointerPressedHandler = Host_PointerPressed;
		_pointerReleasedHandler = Host_PointerReleased;
		_autoScrollTimer = DispatcherQueue.CreateTimer();
		_autoScrollTimer.Interval = TimeSpan.FromMilliseconds(AutoScrollIntervalMilliseconds);
		_autoScrollTimer.IsRepeating = true;
		_autoScrollTimer.Tick += AutoScrollTimer_Tick;
		AddHandler(PointerCanceledEvent, _pointerCanceledHandler, true);
		AddHandler(PointerCaptureLostEvent, _pointerCaptureLostHandler, true);
		AddHandler(PointerMovedEvent, _pointerMovedHandler, true);
		AddHandler(PointerPressedEvent, _pointerPressedHandler, true);
		AddHandler(PointerReleasedEvent, _pointerReleasedHandler, true);
		Unloaded += Host_Unloaded;
	}

	/// <inheritdoc />
	protected override void OnApplyTemplate()
	{
		CompleteSelection(releasePointer: true);
		base.OnApplyTemplate();
		_selectionLayer = GetTemplateChild(SelectionLayerPartName) as Canvas;
		_selectionRectangle = GetTemplateChild(SelectionRectanglePartName) as Rectangle;
	}

	internal void RegisterTarget(FrameworkElement source)
	{
		ArgumentNullException.ThrowIfNull(source);

		if (_state is not SelectionState.Inactive)
		{
			CompleteSelection(releasePointer: true);
		}

		_registeredSources.Add(source);
		RefreshTarget(source);
	}

	internal void UnregisterTarget(FrameworkElement source)
	{
		ArgumentNullException.ThrowIfNull(source);

		if (_state is not SelectionState.Inactive)
		{
			CompleteSelection(releasePointer: true);
		}

		_registeredSources.Remove(source);
		RemoveTarget(source);
	}

	private static RectangleSelectionMode GetSelectionMode(VirtualKeyModifiers modifiers)
	{
		if (modifiers.HasFlag(VirtualKeyModifiers.Control))
		{
			return RectangleSelectionMode.Toggle;
		}

		return modifiers.HasFlag(VirtualKeyModifiers.Shift) ? RectangleSelectionMode.Extend : RectangleSelectionMode.Replace;
	}

	private static bool Intersects(Rect first, Rect second)
	{
		return first.X <= second.X + second.Width && first.X + first.Width >= second.X && first.Y <= second.Y + second.Height && first.Y + first.Height >= second.Y;
	}

	private static double GetAutoScrollDelta(double position, double viewportLength)
	{
		if (position < AutoScrollEdgeSize)
		{
			return -MaximumAutoScrollDelta * Math.Clamp((AutoScrollEdgeSize - position) / AutoScrollEdgeSize, 0, 1);
		}

		if (position > viewportLength - AutoScrollEdgeSize)
		{
			return MaximumAutoScrollDelta * Math.Clamp((position - viewportLength + AutoScrollEdgeSize) / AutoScrollEdgeSize, 0, 1);
		}

		return 0;
	}

	private static ListViewBase? ResolveListView(FrameworkElement source)
	{
		if (source is ListViewBase listView)
		{
			return listView;
		}

		if (source is TableView { RowsHost.Element: ListViewBase tableListView })
		{
			return tableListView;
		}

		return source.FindDescendant<ListViewBase>();
	}

	private bool ApplySelection(TargetState target, HashSet<object> desiredSelection)
	{
		var currentSelection = target.ListView.SelectedItems.Cast<object>().ToHashSet();
		if (currentSelection.SetEquals(desiredSelection))
		{
			return false;
		}

		foreach (var item in currentSelection.Where(item => !desiredSelection.Contains(item)))
		{
			target.ListView.SelectedItems.Remove(item);
		}

		foreach (var item in desiredSelection.Where(item => !currentSelection.Contains(item) && target.ListView.Items.Contains(item)))
		{
			target.ListView.SelectedItems.Add(item);
		}

		return true;
	}

	private void ApplySelections(IReadOnlyDictionary<TargetState, HashSet<object>> desiredSelections)
	{
		var targets = desiredSelections.Keys.Select(static target => target.ListView).Distinct().ToArray();
		var changedTargets = new List<ListViewBase>();
		RectangleSelection.BeginSelectionUpdate(targets);
		try
		{
			foreach (var pair in desiredSelections)
			{
				if (ApplySelection(pair.Key, pair.Value))
				{
					changedTargets.Add(pair.Key.ListView);
				}
			}
		}
		finally
		{
			RectangleSelection.EndSelectionUpdate(targets);
		}

		RectangleSelection.RaiseSelectionUpdated(_notificationModel.RecordChanges(changedTargets));
	}

	private void AutoScrollTimer_Tick(DispatcherQueueTimer sender, object args)
	{
		if (_state is not SelectionState.Active || _scrollOwner is null)
		{
			_autoScrollTimer.Stop();

			return;
		}

		var horizontalOffset = Math.Clamp(_scrollOwner.HorizontalOffset + _autoScrollDelta.X, 0, _scrollOwner.ScrollableWidth);
		var verticalOffset = Math.Clamp(_scrollOwner.VerticalOffset + _autoScrollDelta.Y, 0, _scrollOwner.ScrollableHeight);
		if (horizontalOffset == _scrollOwner.HorizontalOffset && verticalOffset == _scrollOwner.VerticalOffset)
		{
			_autoScrollTimer.Stop();

			return;
		}

		_scrollOwner.ChangeView(horizontalOffset, verticalOffset, null, true);
		UpdateSelection();
		UpdateAutoScrollTimer();
	}

	private void CaptureRealizedItemBounds(TargetState target)
	{
		var viewportBounds = GetViewportBounds();
		foreach (var container in target.RealizedContainers.ToArray())
		{
			if (target.ListView.IndexFromContainer(container) < 0 || target.ListView.ItemFromContainer(container) is not { } item)
			{
				target.RealizedContainers.Remove(container);

				continue;
			}

			var hostBounds = container.TransformToVisual(this).TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
			target.KnownItemBounds[item] = HostToContent(hostBounds, viewportBounds);
		}
	}

	private static void DiscoverRealizedContainers(TargetState target)
	{
		var pendingElements = new Stack<DependencyObject>();
		pendingElements.Push(target.ListView);
		while (pendingElements.Count is not 0)
		{
			var current = pendingElements.Pop();
			var childCount = VisualTreeHelper.GetChildrenCount(current);
			for (var index = 0; index < childCount; index++)
			{
				var child = VisualTreeHelper.GetChild(current, index);
				if (child is SelectorItem container && target.ListView.IndexFromContainer(container) >= 0)
				{
					target.RealizedContainers.Add(container);

					continue;
				}

				pendingElements.Push(child);
			}
		}
	}

	private void CompleteSelection(bool releasePointer)
	{
		var changedTargets = _notificationModel.Complete();
		_autoScrollTimer.Stop();
		if (_selectionRectangle is not null)
		{
			_selectionRectangle.Visibility = Visibility.Collapsed;
			_selectionRectangle.Width = 0;
			_selectionRectangle.Height = 0;
		}

		_state = SelectionState.Inactive;
		_scrollOwner = null;
		foreach (var target in _targets.Values)
		{
			target.KnownItemBounds.Clear();
			target.SelectionModel = null;
		}

		if (releasePointer && _capturedPointer is not null)
		{
			ReleasePointerCapture(_capturedPointer);
		}

		_capturedPointer = null;
		_activeTarget = null;
		RectangleSelection.RaiseSelectionUpdated(changedTargets);
	}

	private TargetState? FindOriginTarget(DependencyObject? source)
	{
		for (var current = source; current is not null && !ReferenceEquals(current, this); current = VisualTreeHelper.GetParent(current))
		{
			foreach (var target in _targets.Values)
			{
				if (ReferenceEquals(current, target.Source) || ReferenceEquals(current, target.ListView))
				{
					return target;
				}
			}
		}

		return null;
	}

	private ScrollViewer? FindScrollOwner()
	{
		if (_targets.Count is 1)
		{
			return _targets.Values.First().ListView.FindDescendant<ScrollViewer>();
		}

		var firstTarget = _targets.Values.FirstOrDefault();
		if (firstTarget is null)
		{
			return null;
		}

		var candidates = GetAncestorScrollViewers(firstTarget.Source);
		foreach (var candidate in candidates)
		{
			if (_targets.Values.All(target => IsAncestor(candidate, target.Source)))
			{
				return candidate;
			}
		}

		return null;
	}

	private IReadOnlyList<ScrollViewer> GetAncestorScrollViewers(DependencyObject source)
	{
		var scrollViewers = new List<ScrollViewer>();
		for (var current = VisualTreeHelper.GetParent(source); current is not null && !ReferenceEquals(current, this); current = VisualTreeHelper.GetParent(current))
		{
			if (current is ScrollViewer scrollViewer)
			{
				scrollViewers.Add(scrollViewer);
			}
		}

		return scrollViewers;
	}

	private Point GetContentPoint(Point hostPoint)
	{
		var viewportBounds = GetViewportBounds();

		return new Point(hostPoint.X - viewportBounds.X + (_scrollOwner?.HorizontalOffset ?? 0), hostPoint.Y - viewportBounds.Y + (_scrollOwner?.VerticalOffset ?? 0));
	}

	private Rect GetSelectionBounds()
	{
		var currentContentPoint = GetContentPoint(ClampToViewport(_pointerPosition));
		var left = Math.Min(_originContentPoint.X, currentContentPoint.X);
		var top = Math.Min(_originContentPoint.Y, currentContentPoint.Y);
		var width = Math.Abs(_originContentPoint.X - currentContentPoint.X);
		var height = Math.Abs(_originContentPoint.Y - currentContentPoint.Y);

		return new Rect(left, top, width, height);
	}

	private Rect GetViewportBounds()
	{
		if (_scrollOwner is null)
		{
			return new Rect(0, 0, ActualWidth, ActualHeight);
		}

		var scrollBounds = _scrollOwner.TransformToVisual(this).TransformBounds(new Rect(0, 0, _scrollOwner.ActualWidth, _scrollOwner.ActualHeight));
		var left = Math.Clamp(scrollBounds.X, 0, ActualWidth);
		var top = Math.Clamp(scrollBounds.Y, 0, ActualHeight);
		var right = Math.Clamp(scrollBounds.X + scrollBounds.Width, 0, ActualWidth);
		var bottom = Math.Clamp(scrollBounds.Y + scrollBounds.Height, 0, ActualHeight);

		return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
	}

	private Rect HostToContent(Rect hostBounds, Rect viewportBounds)
	{
		return new Rect(hostBounds.X - viewportBounds.X + (_scrollOwner?.HorizontalOffset ?? 0), hostBounds.Y - viewportBounds.Y + (_scrollOwner?.VerticalOffset ?? 0), hostBounds.Width, hostBounds.Height);
	}

	private bool IsSelectionStartSource(DependencyObject? source)
	{
		for (var current = source; current is not null && !ReferenceEquals(current, this); current = VisualTreeHelper.GetParent(current))
		{
			if (current is SelectorItem or ButtonBase or ScrollBar or Thumb or ListViewHeaderItem)
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsAncestor(DependencyObject ancestor, DependencyObject descendant)
	{
		for (var current = descendant; current is not null; current = VisualTreeHelper.GetParent(current))
		{
			if (ReferenceEquals(current, ancestor))
			{
				return true;
			}
		}

		return false;
	}

	private Point ClampToViewport(Point point)
	{
		var viewportBounds = GetViewportBounds();

		return new Point(Math.Clamp(point.X, viewportBounds.X, viewportBounds.X + viewportBounds.Width), Math.Clamp(point.Y, viewportBounds.Y, viewportBounds.Y + viewportBounds.Height));
	}

	private void Host_PointerCanceled(object sender, PointerRoutedEventArgs e)
	{
		if (_state is SelectionState.Inactive || _capturedPointer?.PointerId != e.Pointer.PointerId)
		{
			return;
		}

		CompleteSelection(releasePointer: true);
		e.Handled = true;
	}

	private void Host_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
	{
		if (_state is SelectionState.Inactive || _capturedPointer?.PointerId != e.Pointer.PointerId)
		{
			return;
		}

		CompleteSelection(releasePointer: false);
		e.Handled = true;
	}

	private void Host_PointerMoved(object sender, PointerRoutedEventArgs e)
	{
		if (_state is SelectionState.Inactive || _capturedPointer?.PointerId != e.Pointer.PointerId)
		{
			return;
		}

		var currentPoint = e.GetCurrentPoint(this);
		_pointerPosition = currentPoint.Position;
		if (!currentPoint.Properties.IsLeftButtonPressed)
		{
			CompleteSelection(releasePointer: true);
			e.Handled = true;

			return;
		}

		if (_state is SelectionState.Starting)
		{
			var viewportBounds = GetViewportBounds();
			var originHostPoint = new Point(viewportBounds.X + _originContentPoint.X - (_scrollOwner?.HorizontalOffset ?? 0), viewportBounds.Y + _originContentPoint.Y - (_scrollOwner?.VerticalOffset ?? 0));
			if (Math.Abs(originHostPoint.X - _pointerPosition.X) <= DragThreshold && Math.Abs(originHostPoint.Y - _pointerPosition.Y) <= DragThreshold)
			{
				e.Handled = true;

				return;
			}

			_state = SelectionState.Active;
			if (_selectionRectangle is not null)
			{
				_selectionRectangle.Visibility = Visibility.Visible;
			}
		}

		UpdateSelection();
		UpdateAutoScrollTimer();
		e.Handled = true;
	}

	private void Host_PointerPressed(object sender, PointerRoutedEventArgs e)
	{
		if (_state is not SelectionState.Inactive || e.Pointer.PointerDeviceType is not PointerDeviceType.Mouse || !IsSelectionStartSource(e.OriginalSource as DependencyObject))
		{
			return;
		}

		RefreshTargets();
		if (_targets.Count is 0 || _selectionLayer is null || _selectionRectangle is null)
		{
			return;
		}

		var currentPoint = e.GetCurrentPoint(this);
		if (!currentPoint.Properties.IsLeftButtonPressed || !CapturePointer(e.Pointer))
		{
			return;
		}

		_activeTarget = FindOriginTarget(e.OriginalSource as DependencyObject);
		_scrollOwner = FindScrollOwner();
		_capturedPointer = e.Pointer;
		_notificationModel.Reset();
		_pointerPosition = currentPoint.Position;
		_originContentPoint = GetContentPoint(ClampToViewport(_pointerPosition));
		var selectionMode = GetSelectionMode(e.KeyModifiers);
		foreach (var target in _targets.Values)
		{
			target.KnownItemBounds.Clear();
			target.SelectionModel = new RectangleSelectionModel(target.ListView.SelectedItems.Cast<object>(), selectionMode);
			CaptureRealizedItemBounds(target);
		}

		_state = SelectionState.Starting;
		if (selectionMode is RectangleSelectionMode.Replace)
		{
			ApplySelections(_targets.Values.ToDictionary(static target => target, static target => target.SelectionModel!.GetSelection([])));
		}

		_activeTarget?.ListView.Focus(FocusState.Pointer);
		e.Handled = true;
	}

	private void Host_PointerReleased(object sender, PointerRoutedEventArgs e)
	{
		if (_state is SelectionState.Inactive || _capturedPointer?.PointerId != e.Pointer.PointerId)
		{
			return;
		}

		_pointerPosition = e.GetCurrentPoint(this).Position;
		if (_state is SelectionState.Active)
		{
			UpdateSelection();
		}

		var focusTarget = _activeTarget?.ListView;
		CompleteSelection(releasePointer: true);
		focusTarget?.Focus(FocusState.Pointer);
		e.Handled = true;
	}

	private void Host_Unloaded(object sender, RoutedEventArgs e)
	{
		CompleteSelection(releasePointer: true);
	}

	private void RefreshTarget(FrameworkElement source)
	{
		var listView = ResolveListView(source);
		if (listView is null)
		{
			RemoveTarget(source);

			return;
		}

		if (_targets.Any(pair => !ReferenceEquals(pair.Key, source) && ReferenceEquals(pair.Value.ListView, listView)))
		{
			RemoveTarget(source);

			return;
		}

		if (!_targets.TryGetValue(source, out var target) || !ReferenceEquals(target.ListView, listView))
		{
			RemoveTarget(source);
			target = new TargetState(source, listView);
			_targets[source] = target;
			DiscoverRealizedContainers(target);
		}
	}

	private void RefreshTargets()
	{
		foreach (var source in _registeredSources.ToArray())
		{
			RefreshTarget(source);
		}
	}

	private void RemoveTarget(FrameworkElement source)
	{
		if (_targets.Remove(source, out var target))
		{
			target.Dispose();
		}
	}

	private void UpdateAutoScrollTimer()
	{
		if (_scrollOwner is null || _state is not SelectionState.Active)
		{
			_autoScrollTimer.Stop();

			return;
		}

		var viewportBounds = GetViewportBounds();
		var horizontalDelta = GetAutoScrollDelta(_pointerPosition.X - viewportBounds.X, viewportBounds.Width);
		var verticalDelta = GetAutoScrollDelta(_pointerPosition.Y - viewportBounds.Y, viewportBounds.Height);
		if ((_scrollOwner.HorizontalOffset <= 0 && horizontalDelta < 0) || (_scrollOwner.HorizontalOffset >= _scrollOwner.ScrollableWidth && horizontalDelta > 0))
		{
			horizontalDelta = 0;
		}

		if ((_scrollOwner.VerticalOffset <= 0 && verticalDelta < 0) || (_scrollOwner.VerticalOffset >= _scrollOwner.ScrollableHeight && verticalDelta > 0))
		{
			verticalDelta = 0;
		}

		_autoScrollDelta = new Point(horizontalDelta, verticalDelta);
		if (horizontalDelta is 0 && verticalDelta is 0)
		{
			_autoScrollTimer.Stop();

			return;
		}

		if (!_autoScrollTimer.IsRunning)
		{
			_autoScrollTimer.Start();
		}
	}

	private void UpdateSelection()
	{
		var selectionBounds = GetSelectionBounds();
		var desiredSelections = new Dictionary<TargetState, HashSet<object>>();
		foreach (var target in _targets.Values)
		{
			CaptureRealizedItemBounds(target);
			if (target.SelectionModel is not null)
			{
				var intersectedItems = target.KnownItemBounds.Where(pair => Intersects(selectionBounds, pair.Value)).Select(static pair => pair.Key);
				desiredSelections[target] = target.SelectionModel.GetSelection(intersectedItems);
			}
		}

		ApplySelections(desiredSelections);
		UpdateSelectionRectangle(selectionBounds);
	}

	private void UpdateSelectionRectangle(Rect contentBounds)
	{
		if (_selectionRectangle is null)
		{
			return;
		}

		var viewportBounds = GetViewportBounds();
		var viewportLeft = Math.Clamp(contentBounds.X - (_scrollOwner?.HorizontalOffset ?? 0), 0, viewportBounds.Width);
		var viewportTop = Math.Clamp(contentBounds.Y - (_scrollOwner?.VerticalOffset ?? 0), 0, viewportBounds.Height);
		var viewportRight = Math.Clamp(contentBounds.X + contentBounds.Width - (_scrollOwner?.HorizontalOffset ?? 0), 0, viewportBounds.Width);
		var viewportBottom = Math.Clamp(contentBounds.Y + contentBounds.Height - (_scrollOwner?.VerticalOffset ?? 0), 0, viewportBounds.Height);
		Canvas.SetLeft(_selectionRectangle, viewportBounds.X + viewportLeft);
		Canvas.SetTop(_selectionRectangle, viewportBounds.Y + viewportTop);
		_selectionRectangle.Width = Math.Max(0, viewportRight - viewportLeft);
		_selectionRectangle.Height = Math.Max(0, viewportBottom - viewportTop);
	}

	private enum SelectionState
	{
		Inactive,
		Starting,
		Active,
	}

	private sealed class TargetState : IDisposable
	{
		internal Dictionary<object, Rect> KnownItemBounds { get; } = [];

		internal ListViewBase ListView { get; }

		internal HashSet<SelectorItem> RealizedContainers { get; } = [];

		internal FrameworkElement Source { get; }

		internal RectangleSelectionModel? SelectionModel { get; set; }

		internal TargetState(FrameworkElement source, ListViewBase listView)
		{
			Source = source;
			ListView = listView;
			ListView.ContainerContentChanging += ListView_ContainerContentChanging;
		}

		public void Dispose()
		{
			ListView.ContainerContentChanging -= ListView_ContainerContentChanging;
			RealizedContainers.Clear();
		}

		private void ListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
		{
			if (args.ItemContainer is not SelectorItem container)
			{
				return;
			}

			if (args.InRecycleQueue)
			{
				RealizedContainers.Remove(container);
			}
			else
			{
				RealizedContainers.Add(container);
			}
		}
	}
}

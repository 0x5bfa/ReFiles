// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Windows.Foundation;

namespace Files.Controls;

/// <summary>
/// Enables mouse right-button drag initiation for an element that already handles <see cref="UIElement.DragStarting"/>.
/// </summary>
public static class RightButtonDrag
{
	private static readonly ConditionalWeakTable<UIElement, Registration> _registrations = new();

	/// <summary>Identifies the attached enabled property.</summary>
	public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(RightButtonDrag), new PropertyMetadata(false, IsEnabledChanged));

	/// <summary>Gets whether right-button drag initiation is enabled for an element.</summary>
	/// <param name="element">The element to inspect.</param>
	/// <returns><see langword="true"/> when right-button drag initiation is enabled.</returns>
	public static bool GetIsEnabled(DependencyObject element)
	{
		ArgumentNullException.ThrowIfNull(element);

		return (bool)element.GetValue(IsEnabledProperty);
	}

	/// <summary>Sets whether right-button drag initiation is enabled for an element.</summary>
	/// <param name="element">The element to update.</param>
	/// <param name="value">Whether right-button drag initiation is enabled.</param>
	public static void SetIsEnabled(DependencyObject element, bool value)
	{
		ArgumentNullException.ThrowIfNull(element);

		element.SetValue(IsEnabledProperty, value);
	}

	private static void IsEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
	{
		if (sender is not UIElement element)
		{
			return;
		}

		if (args.NewValue is true)
		{
			_registrations.GetValue(element, static target => new Registration(target));

			return;
		}

		if (_registrations.TryGetValue(element, out var registration))
		{
			registration.Dispose();
			_registrations.Remove(element);
		}
	}

	private sealed class Registration : IDisposable
	{
		private const double DragThreshold = 5;

		private readonly UIElement _element;
		private readonly PointerEventHandler _pointerCanceledHandler;
		private readonly PointerEventHandler _pointerCaptureLostHandler;
		private readonly PointerEventHandler _pointerMovedHandler;
		private readonly PointerEventHandler _pointerPressedHandler;
		private readonly PointerEventHandler _pointerReleasedHandler;
		private UIElement? _captureElement;
		private Pointer? _capturedPointer;
		private Point _origin;
		private bool _isStarting;

		internal Registration(UIElement element)
		{
			_element = element;
			_pointerCanceledHandler = Element_PointerCanceled;
			_pointerCaptureLostHandler = Element_PointerCaptureLost;
			_pointerMovedHandler = Element_PointerMoved;
			_pointerPressedHandler = Element_PointerPressed;
			_pointerReleasedHandler = Element_PointerReleased;
			_element.AddHandler(UIElement.PointerCanceledEvent, _pointerCanceledHandler, true);
			_element.AddHandler(UIElement.PointerCaptureLostEvent, _pointerCaptureLostHandler, true);
			_element.AddHandler(UIElement.PointerMovedEvent, _pointerMovedHandler, true);
			_element.AddHandler(UIElement.PointerPressedEvent, _pointerPressedHandler, true);
			_element.AddHandler(UIElement.PointerReleasedEvent, _pointerReleasedHandler, true);
			_element.DragStarting += Element_DragStarting;
		}

		public void Dispose()
		{
			ResetPointer(true);
			_element.RemoveHandler(UIElement.PointerCanceledEvent, _pointerCanceledHandler);
			_element.RemoveHandler(UIElement.PointerCaptureLostEvent, _pointerCaptureLostHandler);
			_element.RemoveHandler(UIElement.PointerMovedEvent, _pointerMovedHandler);
			_element.RemoveHandler(UIElement.PointerPressedEvent, _pointerPressedHandler);
			_element.RemoveHandler(UIElement.PointerReleasedEvent, _pointerReleasedHandler);
			_element.DragStarting -= Element_DragStarting;
		}

		private void Element_DragStarting(UIElement sender, DragStartingEventArgs args)
		{
			ResetPointer(true);
		}

		private void Element_PointerCanceled(object sender, PointerRoutedEventArgs args)
		{
			if (_capturedPointer?.PointerId == args.Pointer.PointerId)
			{
				ResetPointer(false);
			}
		}

		private void Element_PointerCaptureLost(object sender, PointerRoutedEventArgs args)
		{
			if (_capturedPointer?.PointerId == args.Pointer.PointerId)
			{
				ResetPointer(false);
			}
		}

		private async void Element_PointerMoved(object sender, PointerRoutedEventArgs args)
		{
			if (_capturedPointer?.PointerId != args.Pointer.PointerId || _isStarting)
			{
				return;
			}

			var currentPoint = args.GetCurrentPoint(_element);
			if (!currentPoint.Properties.IsRightButtonPressed)
			{
				ResetPointer(true);

				return;
			}

			if (Math.Abs(currentPoint.Position.X - _origin.X) <= DragThreshold && Math.Abs(currentPoint.Position.Y - _origin.Y) <= DragThreshold)
			{
				return;
			}

			_isStarting = true;
			ResetPointer(true);
			args.Handled = true;
			try
			{
				await _element.StartDragAsync(currentPoint);
			}
			catch (Exception exception)
			{
				Debug.WriteLine($"Right-button drag initiation failed: {exception}");
			}
			finally
			{
				_isStarting = false;
			}
		}

		private void Element_PointerPressed(object sender, PointerRoutedEventArgs args)
		{
			if (_capturedPointer is not null || _isStarting || !_element.CanDrag || args.Pointer.PointerDeviceType is not PointerDeviceType.Mouse)
			{
				return;
			}

			var currentPoint = args.GetCurrentPoint(_element);
			var captureElement = args.OriginalSource as UIElement ?? _element;
			if (!currentPoint.Properties.IsRightButtonPressed || currentPoint.Properties.IsLeftButtonPressed || !captureElement.CapturePointer(args.Pointer))
			{
				return;
			}

			_captureElement = captureElement;
			_capturedPointer = args.Pointer;
			_origin = currentPoint.Position;
		}

		private void Element_PointerReleased(object sender, PointerRoutedEventArgs args)
		{
			if (_capturedPointer?.PointerId == args.Pointer.PointerId)
			{
				ResetPointer(true);
			}
		}

		private void ResetPointer(bool releaseCapture)
		{
			var captureElement = _captureElement;
			var pointer = _capturedPointer;
			_captureElement = null;
			_capturedPointer = null;
			if (releaseCapture && captureElement is not null && pointer is not null)
			{
				captureElement.ReleasePointerCapture(pointer);
			}
		}
	}
}

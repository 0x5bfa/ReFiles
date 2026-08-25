// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Controls;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Windows.UI;

namespace Files.UITests.ControlTests;

/// <summary>
/// Verifies themed icon rendering behavior in a live WinUI visual tree.
/// </summary>
[TestClass]
public sealed class ThemedIconTests
{
	/// <summary>
	/// Verifies that an icon uses its requested theme when its rendered palette changes at runtime.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task RenderedPaletteFollowsRequestedTheme()
	{
		if (Application.Current.Resources.TryGetValue("ThemedIconHighContrast", out var highContrastValue) && highContrastValue is true)
		{
			Assert.Inconclusive("Requested Light and Dark themes are overridden while the system is using high contrast.");
		}

		var icon = new ThemedIcon
		{
			Data = new ThemedIconData { OutlineData = "M0,0 H16 V16 H0 Z", Size = 16 },
			IconType = ThemedIconTypes.Outline,
		};
		var host = new Grid();
		host.Children.Add(icon);
		var window = new Window { Content = host };
		IAnimatedVisual? animatedVisual = null;
		try
		{
			var loaded = WaitForLoadedAsync(icon);
			window.Activate();
			await loaded;

			var darkThemeChanged = WaitForActualThemeAsync(icon, ElementTheme.Dark);
			icon.RequestedTheme = ElementTheme.Dark;
			await darkThemeChanged;

			var brush = CreateTrackedBrush(icon, out animatedVisual);
			Assert.AreEqual(Color.FromArgb(219, 240, 240, 240), brush.Color);
			animatedVisual.Dispose();
			animatedVisual = null;

			var lightThemeChanged = WaitForActualThemeAsync(icon, ElementTheme.Light);
			icon.RequestedTheme = ElementTheme.Light;
			await lightThemeChanged;

			brush = CreateTrackedBrush(icon, out animatedVisual);
			Assert.AreEqual(Color.FromArgb(219, 22, 22, 22), brush.Color);
		}
		finally
		{
			animatedVisual?.Dispose();
			window.Close();
		}
	}

	private static CompositionColorBrush CreateTrackedBrush(ThemedIcon icon, out IAnimatedVisual animatedVisual)
	{
		var visualSource = icon.Source as ThemedIconVisualSource;
		Assert.IsNotNull(visualSource);
		var compositor = ElementCompositionPreview.GetElementVisual(icon).Compositor;
		animatedVisual = visualSource.TryCreateAnimatedVisual(compositor, out var diagnostics);
		Assert.IsNotNull(animatedVisual, diagnostics?.ToString());
		var rootVisual = animatedVisual.RootVisual as ShapeVisual;
		Assert.IsNotNull(rootVisual);
		Assert.AreEqual(1, rootVisual.Shapes.Count);
		var spriteShape = rootVisual.Shapes[0] as CompositionSpriteShape;
		Assert.IsNotNull(spriteShape);
		var brush = spriteShape.FillBrush as CompositionColorBrush;
		Assert.IsNotNull(brush);

		return brush;
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

	private static async Task WaitForActualThemeAsync(FrameworkElement element, ElementTheme expectedTheme)
	{
		if (element.ActualTheme == expectedTheme)
		{
			return;
		}

		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		void OnActualThemeChanged(FrameworkElement sender, object args)
		{
			if (sender.ActualTheme == expectedTheme)
			{
				completion.TrySetResult();
			}
		}

		element.ActualThemeChanged += OnActualThemeChanged;
		try
		{
			if (element.ActualTheme == expectedTheme)
			{
				completion.TrySetResult();
			}

			await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
		}
		finally
		{
			element.ActualThemeChanged -= OnActualThemeChanged;
		}
	}
}

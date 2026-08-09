// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Files.ControlTests.Views
{
	public sealed partial class ThemedIconPerformancePage : Page
	{
		private const int CellSize = 24;
		private const int ColumnCount = 32;
		private const int FrameCount = 2;
		private const int MaxIconCount = 10_000;
		private const int MeasuredRunCount = 5;
		private const string IconResourceKey = "App.ThemedIcons.SetSlideshow.16";
		private readonly Action<FrameworkElement> _configureIcon;
		private readonly Func<FrameworkElement> _createIcon;

		public ThemedIconPerformancePage()
		{
			InitializeComponent();

			var definition = GetIconDefinition();
			var iconType = GetThemedIconType();
			_createIcon = CreateIconFactory(iconType);
			_configureIcon = CreateIconConfigurator(iconType, definition);
		}

		private async void RunButton_Click(object sender, RoutedEventArgs e)
		{
			var requestedCount = IconCountBox.Value;
			if (!double.IsFinite(requestedCount) || requestedCount < 1 || requestedCount > MaxIconCount || requestedCount != Math.Truncate(requestedCount))
			{
				ResultText.Text = $"Enter an integer between 1 and {MaxIconCount:N0}.";

				return;
			}

			RunButton.IsEnabled = false;
			ProgressRing.IsActive = true;

			try
			{
				var measurements = await RunMeasurementsAsync((int)requestedCount);
				ResultText.Text = FormatResults((int)requestedCount, measurements);
			}
			catch (Exception exception)
			{
				ResultText.Text = $"Measurement failed: {exception.Message}";
			}
			finally
			{
				RunButton.IsEnabled = true;
				ProgressRing.IsActive = false;
			}
		}

		private static object GetIconDefinition()
		{
			if (Application.Current?.Resources.TryGetValue(IconResourceKey, out var definition) is not true || definition is null)
			{
				throw new InvalidOperationException($"The resource '{IconResourceKey}' was not found.");
			}

			return definition;
		}

		[DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicProperties, "Files.Controls.ThemedIcon", "Files.Controls")]
		[return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicProperties)]
		private static Type GetThemedIconType()
		{
			var iconType = Type.GetType("Files.Controls.ThemedIcon, Files.Controls");
			if (iconType is null)
			{
				throw new InvalidOperationException("The Files.Controls.ThemedIcon type was not found.");
			}

			return iconType;
		}

		private static Func<FrameworkElement> CreateIconFactory(
			[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicProperties)] Type iconType)
		{
			if (!typeof(FrameworkElement).IsAssignableFrom(iconType))
			{
				throw new InvalidOperationException("ThemedIcon is not a FrameworkElement.");
			}

			var icon = Expression.Convert(Expression.New(iconType), typeof(FrameworkElement));

			return Expression.Lambda<Func<FrameworkElement>>(icon).Compile();
		}

		private static Action<FrameworkElement> CreateIconConfigurator(
			[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicProperties)] Type iconType,
			object definition)
		{
			if (definition is Style style)
			{
				return icon => icon.Style = style;
			}

			var dataProperty = iconType.GetProperty("Data");
			if (dataProperty is null || !dataProperty.CanWrite || !dataProperty.PropertyType.IsInstanceOfType(definition))
			{
				throw new InvalidOperationException("ThemedIcon does not expose a compatible Data property.");
			}

			var iconParameter = Expression.Parameter(typeof(FrameworkElement), "icon");
			var property = Expression.Property(Expression.Convert(iconParameter, iconType), dataProperty);
			var value = Expression.Constant(definition, dataProperty.PropertyType);
			var assignment = Expression.Assign(property, value);

			return Expression.Lambda<Action<FrameworkElement>>(assignment, iconParameter).Compile();
		}

		private async Task<IReadOnlyList<Measurement>> RunMeasurementsAsync(int count)
		{
			await ResetCanvasAsync();
			BuildIcons(count);
			await WaitForRenderingAsync();
			await ResetCanvasAsync();

			var measurements = new List<Measurement>(MeasuredRunCount);
			for (var run = 0; run < MeasuredRunCount; run++)
			{
				await ResetCanvasAsync();
				PrepareForMeasurement();
				measurements.Add(await MeasureAsync(count));
			}

			return measurements;
		}

		private async Task<Measurement> MeasureAsync(int count)
		{
			var threadAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
			var totalAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
			var startTimestamp = Stopwatch.GetTimestamp();

			BuildIcons(count);
			var buildMilliseconds = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
			await WaitForRenderingAsync();

			return new Measurement(
				buildMilliseconds,
				Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
				GC.GetAllocatedBytesForCurrentThread() - threadAllocatedBefore,
				GC.GetTotalAllocatedBytes(precise: true) - totalAllocatedBefore);
		}

		private void BuildIcons(int count)
		{
			var rowCount = (count + ColumnCount - 1) / ColumnCount;
			IconCanvas.Width = ColumnCount * CellSize;
			IconCanvas.Height = rowCount * CellSize;

			for (var index = 0; index < count; index++)
			{
				var icon = _createIcon();
				_configureIcon(icon);
				icon.Width = 16;
				icon.Height = 16;
				Canvas.SetLeft(icon, index % ColumnCount * CellSize + 4);
				Canvas.SetTop(icon, index / ColumnCount * CellSize + 4);
				IconCanvas.Children.Add(icon);
			}
		}

		private async Task ResetCanvasAsync()
		{
			IconCanvas.Children.Clear();
			IconCanvas.Width = 0;
			IconCanvas.Height = 0;
			await WaitForRenderingAsync();
		}

		private static void PrepareForMeasurement()
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
		}

		private static Task WaitForRenderingAsync()
		{
			var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var frameCount = 0;
			EventHandler<object>? handler = null;
			handler = (sender, args) =>
			{
				frameCount++;
				if (frameCount < FrameCount)
				{
					return;
				}

				CompositionTarget.Rendering -= handler;
				completion.SetResult();
			};
			CompositionTarget.Rendering += handler;

			return completion.Task;
		}

		private static string FormatResults(int count, IReadOnlyList<Measurement> measurements)
		{
			var lines = new List<string>
			{
				$"Count: {count:N0} (non-virtualized Canvas)",
				$"Runs: {measurements.Count} (warm-up excluded)",
			};

			for (var index = 0; index < measurements.Count; index++)
			{
				var measurement = measurements[index];
				var runSummary = $"Run {index + 1}: build {measurement.BuildMilliseconds:N1} ms, to Rendering frame 2 {measurement.DisplayMilliseconds:N1} ms, " +
					$"UI managed {FormatBytes(measurement.UiThreadAllocatedBytes)}, process managed {FormatBytes(measurement.TotalAllocatedBytes)}";
				lines.Add(runSummary);
			}

			var medianBuildMilliseconds = GetMedian(measurements.Select(static measurement => measurement.BuildMilliseconds));
			var medianDisplayMilliseconds = GetMedian(measurements.Select(static measurement => measurement.DisplayMilliseconds));
			var medianUiThreadAllocatedBytes = GetMedian(measurements.Select(static measurement => measurement.UiThreadAllocatedBytes));
			var medianTotalAllocatedBytes = GetMedian(measurements.Select(static measurement => measurement.TotalAllocatedBytes));
			var medianSummary = $"Median: build {medianBuildMilliseconds:N1} ms, to Rendering frame 2 {medianDisplayMilliseconds:N1} ms, " +
				$"UI managed {FormatBytes(medianUiThreadAllocatedBytes)}, process managed {FormatBytes(medianTotalAllocatedBytes)}";
			lines.Add(medianSummary);
			lines.Add("Native Win2D/Composition allocations are not included in the managed allocation values.");

			return string.Join(Environment.NewLine, lines);
		}

		private static double GetMedian(IEnumerable<double> values)
		{
			var ordered = values.OrderBy(static value => value).ToArray();

			return ordered[ordered.Length / 2];
		}

		private static long GetMedian(IEnumerable<long> values)
		{
			var ordered = values.OrderBy(static value => value).ToArray();

			return ordered[ordered.Length / 2];
		}

		private static string FormatBytes(long bytes) => $"{bytes / 1024d:N1} KiB";

		private sealed record Measurement(double BuildMilliseconds, double DisplayMilliseconds, long UiThreadAllocatedBytes, long TotalAllocatedBytes);
	}
}

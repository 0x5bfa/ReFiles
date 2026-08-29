// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Specialized;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace Files.Controls;

/// <summary>
/// Renders an operation-rate history against progress using Windows Composition.
/// </summary>
public sealed partial class SpeedGraph : Control
{
	private const int AxisSampleWindow = 37;
	private const float GraphBottomInset = 4f;
	private const float GraphTopInset = 4f;
	private const float MinimumAxisMaximum = 1f;
	private const float TargetRateHeightRatio = 0.7f;
	private const string AccentBrushResourceKey = "AccentFillColorDefaultBrush";
	private const string BackgroundBrushResourceKey = "ControlFillColorTransparentBrush";
	private static readonly Color _defaultAccentColor = Color.FromArgb(255, 0, 120, 212);
	private static readonly TimeSpan _graphUpdateInterval = TimeSpan.FromMilliseconds(100);

	private readonly Compositor _compositor;
	private readonly ObservableCollection<Vector2> _emptyPoints = [];
	private readonly DispatcherQueueTimer _updateTimer;
	private CanvasGeometry? _canvasGeometry;
	private ContainerVisual? _rootVisual;
	private RectangleClip? _rootClip;
	private SpriteVisual? _backgroundVisual;
	private ShapeVisual? _graphVisual;
	private CompositionPathGeometry? _graphGeometry;
	private InsetClip? _graphClip;
	private SpriteVisual? _lineVisual;
	private CompositionColorBrush? _backgroundBrush;
	private CompositionLinearGradientBrush? _graphFillBrush;
	private CompositionColorGradientStop? _graphFillTop;
	private CompositionColorGradientStop? _graphFillBottom;
	private CompositionColorBrush? _graphStrokeBrush;
	private LinearEasingFunction? _linearEasing;
	private ObservableCollection<Vector2>? _observedPoints;
	private long _foregroundCallbackToken;
	private bool _foregroundCallbackRegistered;
	private bool _isInitialized;
	private bool _isUpdateQueued;
	private float _width;
	private float _height;
	private float _maximumSpeed = MinimumAxisMaximum;

	/// <summary>
	/// Identifies the <see cref="Points"/> dependency property.
	/// </summary>
	public static readonly DependencyProperty PointsProperty =
		DependencyProperty.Register(nameof(Points), typeof(ObservableCollection<Vector2>), typeof(SpeedGraph), new PropertyMetadata(null, OnPointsPropertyChanged));

	/// <summary>
	/// Identifies the <see cref="ProgressPercentage"/> dependency property.
	/// </summary>
	public static readonly DependencyProperty ProgressPercentageProperty =
		DependencyProperty.Register(nameof(ProgressPercentage), typeof(double), typeof(SpeedGraph), new PropertyMetadata(0d, OnProgressPercentagePropertyChanged));

	/// <summary>
	/// Initializes a new instance of the <see cref="SpeedGraph"/> class.
	/// </summary>
	public SpeedGraph()
	{
		_compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
		_updateTimer = DispatcherQueue.CreateTimer();
		_updateTimer.Interval = _graphUpdateInterval;
		_updateTimer.IsRepeating = false;
		_updateTimer.Tick += OnUpdateTimerTick;
		Points = [];
		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
		SizeChanged += OnSizeChanged;
		ActualThemeChanged += OnActualThemeChanged;
		RegisterForegroundCallback();
	}

	/// <summary>
	/// Gets or sets the progress and rate points rendered by the graph. Each point stores progress from 0 to 100 in X and bytes or items per second in Y.
	/// </summary>
	public ObservableCollection<Vector2> Points
	{
		get => (ObservableCollection<Vector2>?)GetValue(PointsProperty) ?? _emptyPoints;
		set => SetValue(PointsProperty, value ?? []);
	}

	/// <summary>
	/// Gets or sets the current operation progress, expressed as a percentage from 0 to 100.
	/// </summary>
	public double ProgressPercentage
	{
		get => (double)GetValue(ProgressPercentageProperty);
		set => SetValue(ProgressPercentageProperty, value);
	}

	private static void OnPointsPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
	{
		var graph = (SpeedGraph)sender;
		graph.ChangePoints((ObservableCollection<Vector2>?)args.OldValue, (ObservableCollection<Vector2>?)args.NewValue);
	}

	private static void OnProgressPercentagePropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
	{
		((SpeedGraph)sender).QueueGraphUpdate();
	}

	private void OnLoaded(object sender, RoutedEventArgs args)
	{
		RegisterForegroundCallback();
		SubscribePoints();
		EnsureGraph();
		UpdateGraphColors();
		QueueGraphUpdate();
	}

	private void OnUnloaded(object sender, RoutedEventArgs args)
	{
		_updateTimer.Stop();
		_isUpdateQueued = false;
		UnsubscribePoints();
		UnregisterForegroundCallback();
		DisposeGraph();
	}

	private void OnSizeChanged(object sender, SizeChangedEventArgs args)
	{
		if (!IsValidSize(args.NewSize))
		{
			return;
		}

		EnsureGraph();
		UpdateGraphSize(args.NewSize);
		UpdateGraph();
	}

	private void OnActualThemeChanged(FrameworkElement sender, object args)
	{
		UpdateGraphColors();
	}

	private void OnForegroundChanged(DependencyObject sender, DependencyProperty property)
	{
		UpdateGraphColors();
	}

	private void ChangePoints(ObservableCollection<Vector2>? oldPoints, ObservableCollection<Vector2>? newPoints)
	{
		if (ReferenceEquals(_observedPoints, oldPoints))
		{
			UnsubscribePoints();
		}

		if (IsLoaded)
		{
			SubscribePoints();
		}

		QueueGraphUpdate();
	}

	private void SubscribePoints()
	{
		var points = (ObservableCollection<Vector2>?)GetValue(PointsProperty);
		if (ReferenceEquals(_observedPoints, points))
		{
			return;
		}

		UnsubscribePoints();
		if (points is null)
		{
			return;
		}

		_observedPoints = points;
		_observedPoints.CollectionChanged += OnPointsChanged;
	}

	private void UnsubscribePoints()
	{
		if (_observedPoints is null)
		{
			return;
		}

		_observedPoints.CollectionChanged -= OnPointsChanged;
		_observedPoints = null;
	}

	private void OnPointsChanged(object? sender, NotifyCollectionChangedEventArgs args)
	{
		QueueGraphUpdate();
	}

	private void QueueGraphUpdate()
	{
		if (!_isInitialized || _isUpdateQueued)
		{
			return;
		}

		_isUpdateQueued = true;
		_updateTimer.Start();
	}

	private void OnUpdateTimerTick(DispatcherQueueTimer sender, object args)
	{
		_isUpdateQueued = false;
		UpdateGraph();
	}

	private void RegisterForegroundCallback()
	{
		if (_foregroundCallbackRegistered)
		{
			return;
		}

		_foregroundCallbackToken = RegisterPropertyChangedCallback(ForegroundProperty, OnForegroundChanged);
		_foregroundCallbackRegistered = true;
	}

	private void UnregisterForegroundCallback()
	{
		if (!_foregroundCallbackRegistered)
		{
			return;
		}

		UnregisterPropertyChangedCallback(ForegroundProperty, _foregroundCallbackToken);
		_foregroundCallbackToken = 0;
		_foregroundCallbackRegistered = false;
	}

	private void EnsureGraph()
	{
		if (_isInitialized || !IsValidSize(new Size(ActualWidth, ActualHeight)))
		{
			return;
		}

		_width = (float)ActualWidth;
		_height = (float)ActualHeight;
		_rootVisual = _compositor.CreateContainerVisual();
		_rootVisual.Size = new Vector2(_width, _height);
		_rootClip = _compositor.CreateRectangleClip();
		_rootVisual.Clip = _rootClip;

		_backgroundBrush = _compositor.CreateColorBrush();
		_backgroundVisual = _compositor.CreateSpriteVisual();
		_backgroundVisual.Size = _rootVisual.Size;
		_backgroundVisual.Brush = _backgroundBrush;

		_graphFillBrush = _compositor.CreateLinearGradientBrush();
		_graphFillBrush.StartPoint = new Vector2(0.5f, 0f);
		_graphFillBrush.EndPoint = new Vector2(0.5f, 1f);
		_graphFillTop = _compositor.CreateColorGradientStop();
		_graphFillTop.Offset = 0f;
		_graphFillBottom = _compositor.CreateColorGradientStop();
		_graphFillBottom.Offset = 1f;
		_graphFillBrush.ColorStops.Add(_graphFillBottom);
		_graphFillBrush.ColorStops.Add(_graphFillTop);
		_graphStrokeBrush = _compositor.CreateColorBrush();
		_graphGeometry = _compositor.CreatePathGeometry();

		_graphVisual = _compositor.CreateShapeVisual();
		_graphVisual.Size = _rootVisual.Size;
		var graphShape = _compositor.CreateSpriteShape();
		graphShape.FillBrush = _graphFillBrush;
		graphShape.StrokeBrush = _graphStrokeBrush;
		graphShape.StrokeThickness = 1f;
		graphShape.Geometry = _graphGeometry;
		_graphVisual.Shapes.Add(graphShape);

		_graphClip = _compositor.CreateInsetClip();
		_graphClip.RightInset = _width;
		_graphVisual.Clip = _graphClip;

		_lineVisual = _compositor.CreateSpriteVisual();
		_lineVisual.Size = new Vector2(_width, 1.5f);
		_lineVisual.Brush = _graphStrokeBrush;

		_linearEasing = _compositor.CreateLinearEasingFunction();
		_rootVisual.Children.InsertAtBottom(_backgroundVisual);
		_rootVisual.Children.InsertAtBottom(_graphVisual);
		_rootVisual.Children.InsertAtTop(_lineVisual);
		ElementCompositionPreview.SetElementChildVisual(this, _rootVisual);
		_isInitialized = true;
		UpdateRootClip();
	}

	private void UpdateGraphSize(Size newSize)
	{
		if (!_isInitialized || _rootVisual is null)
		{
			return;
		}

		_width = (float)newSize.Width;
		_height = (float)newSize.Height;
		var size = new Vector2(_width, _height);
		_rootVisual.Size = size;
		_backgroundVisual!.Size = size;
		_graphVisual!.Size = size;
		_lineVisual!.Size = new Vector2(_width, 1.5f);
		UpdateRootClip();
	}

	private void UpdateRootClip()
	{
		if (_rootClip is null)
		{
			return;
		}

		_rootClip.Top = 1.5f;
		_rootClip.Left = 1.5f;
		_rootClip.Right = Math.Max(1.5f, _width - 2f);
		_rootClip.Bottom = Math.Max(1.5f, _height - 1.5f);
		_rootClip.TopLeftRadius = new Vector2(4f, 4f);
		_rootClip.TopRightRadius = new Vector2(4f, 4f);
		_rootClip.BottomLeftRadius = new Vector2(4f, 4f);
		_rootClip.BottomRightRadius = new Vector2(4f, 4f);
	}

	private void UpdateGraph()
	{
		if (!_isInitialized || _graphGeometry is null || _graphClip is null || _lineVisual is null || _linearEasing is null)
		{
			return;
		}

		var points = GetValidPoints();
		if (points.Count is 0)
		{
			_graphGeometry.Path = null;
			_canvasGeometry?.Dispose();
			_canvasGeometry = null;
			_graphClip.RightInset = _width;
			_lineVisual.Offset = new Vector3(0, _height - GraphBottomInset, 0);

			return;
		}

		var targetMaximumSpeed = GetMaximumSpeed(points);
		_maximumSpeed = targetMaximumSpeed >= _maximumSpeed ? targetMaximumSpeed : Math.Max(targetMaximumSpeed, _maximumSpeed * 0.9f);
		var lastPoint = points[^1];
		using var pathBuilder = new CanvasPathBuilder(null);
		pathBuilder.BeginFigure(0f, _height);
		foreach (var point in points)
		{
			pathBuilder.AddLine(GetX(point.X), GetY(point.Y));
		}

		var lastX = GetX(lastPoint.X);
		pathBuilder.AddLine(Math.Min(_width, lastX + 2f), GetY(lastPoint.Y));
		pathBuilder.AddLine(Math.Min(_width, lastX + 2f), _height);
		pathBuilder.EndFigure(CanvasFigureLoop.Closed);
		var canvasGeometry = CanvasGeometry.CreatePath(pathBuilder);
		_graphGeometry.Path = new CompositionPath(canvasGeometry);
		_canvasGeometry?.Dispose();
		_canvasGeometry = canvasGeometry;

		using var lineAnimation = _compositor.CreateScalarKeyFrameAnimation();
		lineAnimation.InsertKeyFrame(1f, GetY(lastPoint.Y), _linearEasing);
		lineAnimation.Duration = TimeSpan.FromMilliseconds(72);
		_lineVisual.StartAnimation("Offset.Y", lineAnimation);

		using var clipAnimation = _compositor.CreateScalarKeyFrameAnimation();
		clipAnimation.InsertKeyFrame(1f, GetRightInset(GetCurrentProgressPercentage(lastPoint.X)), _linearEasing);
		clipAnimation.Duration = TimeSpan.FromMilliseconds(72);
		_graphClip.StartAnimation("RightInset", clipAnimation);
	}

	private List<Vector2> GetValidPoints()
	{
		var points = new List<Vector2>(Points.Count);
		foreach (var point in Points)
		{
			if (float.IsFinite(point.X) && float.IsFinite(point.Y))
			{
				points.Add(new Vector2(Math.Clamp(point.X, 0f, 100f), Math.Max(0f, point.Y)));
			}
		}

		return points;
	}

	private static float GetMaximumSpeed(IReadOnlyList<Vector2> points)
	{
		var maximumSpeed = MinimumAxisMaximum;
		var startIndex = Math.Max(0, points.Count - AxisSampleWindow);
		for (var index = startIndex; index < points.Count; index++)
		{
			maximumSpeed = Math.Max(maximumSpeed, points[index].Y);
		}

		var paddedMaximum = maximumSpeed / TargetRateHeightRatio;

		return float.IsFinite(paddedMaximum) ? paddedMaximum : maximumSpeed;
	}

	private float GetX(float progressPercentage)
	{
		return _width * progressPercentage / 100f;
	}

	private float GetY(float speed)
	{
		var chartHeight = Math.Max(1f, _height - GraphTopInset - GraphBottomInset);

		return _height - GraphBottomInset - Math.Clamp(speed / _maximumSpeed, 0f, 1f) * chartHeight;
	}

	private float GetRightInset(float progressPercentage)
	{
		var percentage = Math.Clamp(progressPercentage, 0f, 100f);

		return Math.Clamp(_width - (_width * percentage / 100f), 0f, _width);
	}

	private float GetCurrentProgressPercentage(float fallback)
	{
		if (double.IsFinite(ProgressPercentage) && ProgressPercentage > 0)
		{
			return (float)ProgressPercentage;
		}

		return fallback;
	}

	private void UpdateGraphColors()
	{
		if (_backgroundBrush is null || _graphFillTop is null || _graphFillBottom is null || _graphStrokeBrush is null)
		{
			return;
		}

		var accentColor = GetAccentColor();
		var transparentColor = Color.FromArgb(0x12, accentColor.R, accentColor.G, accentColor.B);
		var fillColor = ActualTheme is ElementTheme.Light
			? Color.FromArgb(0x55, accentColor.R, accentColor.G, accentColor.B)
			: Color.FromArgb(0x7f, accentColor.R, accentColor.G, accentColor.B);
		_backgroundBrush.Color = GetResourceColor(BackgroundBrushResourceKey, transparentColor);
		_graphFillTop.Color = fillColor;
		_graphFillBottom.Color = transparentColor;
		_graphStrokeBrush.Color = accentColor;
	}

	private Color GetAccentColor()
	{
		if (ReadLocalValue(ForegroundProperty) is SolidColorBrush foregroundBrush)
		{
			var color = foregroundBrush.Color;
			color.A = (byte)Math.Clamp(color.A * foregroundBrush.Opacity, 0, 255);

			return color;
		}

		return GetResourceColor(AccentBrushResourceKey, _defaultAccentColor);
	}

	private static Color GetResourceColor(string key, Color fallback)
	{
		if (Application.Current?.Resources.TryGetValue(key, out var value) is true)
		{
			if (value is SolidColorBrush brush)
			{
				return brush.Color;
			}

			if (value is Color color)
			{
				return color;
			}
		}

		return fallback;
	}

	private static bool IsValidSize(Size size)
	{
		return double.IsFinite(size.Width) && size.Width > 0 && double.IsFinite(size.Height) && size.Height > 0;
	}

	private void DisposeGraph()
	{
		if (!_isInitialized)
		{
			return;
		}

		ElementCompositionPreview.SetElementChildVisual(this, null!);
		_rootVisual?.Dispose();
		_canvasGeometry?.Dispose();
		_rootClip?.Dispose();
		_backgroundVisual?.Dispose();
		_graphVisual?.Dispose();
		_graphGeometry?.Dispose();
		_graphClip?.Dispose();
		_lineVisual?.Dispose();
		_backgroundBrush?.Dispose();
		_graphFillBrush?.Dispose();
		_graphFillTop?.Dispose();
		_graphFillBottom?.Dispose();
		_graphStrokeBrush?.Dispose();
		_linearEasing?.Dispose();
		_rootVisual = null;
		_canvasGeometry = null;
		_rootClip = null;
		_backgroundVisual = null;
		_graphVisual = null;
		_graphGeometry = null;
		_graphClip = null;
		_lineVisual = null;
		_backgroundBrush = null;
		_graphFillBrush = null;
		_graphFillTop = null;
		_graphFillBottom = null;
		_graphStrokeBrush = null;
		_linearEasing = null;
		_maximumSpeed = MinimumAxisMaximum;
		_isInitialized = false;
		_isUpdateQueued = false;
	}
}

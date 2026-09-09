// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using System.Runtime.InteropServices;
using Files.Core.Browsing;
using Files.Core.Capabilities.Previews;
using Files.Localization;
using Files.ViewModels;
using Files.Core.Windows;
using Microsoft.UI.Content;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.DirectComposition;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT.Interop;
using WNDPROC = Windows.Win32.Extras.ManagedWNDPROC;

namespace Files.Views;

public sealed partial class PreviewPane : UserControl, IDisposable, IAsyncDisposable
{
	private const string PreviewHostWindowClassPrefix = "ReFilesPreviewHost";

	private const int PreviewAcceleratorQueueLimit = 8;
	private const uint WindowMessageKeyDown = 0x0100;
	private const uint WindowMessageSystemKeyDown = 0x0104;
	private const uint VirtualKeyTab = 0x09;
	private const int VirtualKeyShift = 0x10;
	private const int VirtualKeyControl = 0x11;
	private const int VirtualKeyMenu = 0x12;
	private const uint VirtualKeyA = 0x41;
	private const uint VirtualKeyZ = 0x5A;
	private const uint VirtualKeyF1 = 0x70;
	private const uint VirtualKeyF6 = 0x75;
	private const uint VirtualKeyF12 = 0x7B;
	private static readonly TimeSpan _previewFocusQueryTimeout = TimeSpan.FromSeconds(5);

	private readonly SemaphoreSlim _renderGate = new(1, 1);
	private readonly Lock _lifecycleLock = new();
	private readonly Queue<PreviewAcceleratorRequest> _pendingPreviewAccelerators = new();
	private readonly long _visibilityChangedToken;
	private readonly DispatcherQueue _dispatcherQueue;
	private readonly PointerEventHandler _previewRootPointerChangedHandler;

	private PreviewPaneViewModel? _subscribedViewModel;
	private IWindowsShellPreviewSession? _shellSession;
	private CancellationTokenSource? _renderCancellation;
	private Task? _cleanupTask;
	private Task? _disposeTask;

	private HWND _previewHost;
	private HWND _windowHandle;
	private UIElement? _inputRoot;
	private XamlRoot? _subscribedXamlRoot;
	private ContentExternalOutputLink? _contentExternalOutputLink;
	private ID3D11Device? _d3d11Device;
	private ID3D11DeviceContext? _d3d11DeviceContext;
	private IDCompositionDevice? _compositionDevice;
	private IDCompositionVisual? _previewVisual;
	private object? _previewSurface;
	private readonly string _previewHostClassName = $"{PreviewHostWindowClassPrefix}_{Guid.NewGuid():N}";
	private WNDPROC? _previewHostWindowProc;
	private HINSTANCE _previewHostWindowInstance;
	private PreviewHostLayout? _appliedHostLayout;
	private long _renderVersion;
	private int _layoutUpdateQueued;
	private int _isMovingFocusFromPreview;
	private int _isDisposed;
	private bool _isForwardingPreviewAccelerator;
	private bool _waitingForShellLayout;
	private bool _isPreviewHostCloaked;

	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.Register(nameof(ViewModel), typeof(PreviewPaneViewModel), typeof(PreviewPane), new PropertyMetadata(null, ViewModelChanged));

	public PreviewPaneViewModel? ViewModel
	{
		get => (PreviewPaneViewModel?)GetValue(ViewModelProperty);
		set => SetValue(ViewModelProperty, value);
	}

	public IWindowsShellPreviewSessionFactory? SessionFactory { get; set; }

	internal HWND PreviewHostWindowHandle => _previewHost;

	internal bool HasShellPreviewSession => _shellSession is not null;

	internal bool HasShellPreviewComposition => _contentExternalOutputLink is not null;

	internal bool HasAppliedShellPreviewLayout => _appliedHostLayout is not null;

	internal bool IsShellPreviewVisualReady => HasShellPreviewSession && HasShellPreviewComposition && HasAppliedShellPreviewLayout;

	public PreviewPane()
	{
		InitializeComponent();
		_dispatcherQueue = DispatcherQueue;
		_previewRootPointerChangedHandler = PreviewRoot_PointerChanged;
		PreviewTitleBlock.Text = Strings.Preview.GetLocalized();
		PreviewAnywayButton.Content = Strings.PreviewAnyway.GetLocalized();
		Loaded += PreviewPane_Loaded;
		Unloaded += PreviewPane_Unloaded;
		GotFocus += PreviewPane_GotFocus;
		PreviewSurface.LayoutUpdated += PreviewSurface_LayoutUpdated;
		_visibilityChangedToken = RegisterPropertyChangedCallback(VisibilityProperty, PreviewPane_VisibilityChanged);
	}

	public void AttachWindow(Window window)
	{
		ArgumentNullException.ThrowIfNull(window);
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) is not 0, this);

		_windowHandle = (HWND)WindowNative.GetWindowHandle(window);
		if (IsLoaded)
		{
			_ = RenderCurrentAsync();
		}
	}

	public void Dispose()
	{
		_ = DisposeAsync();
	}

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is 0)
		{
			Loaded -= PreviewPane_Loaded;
			Unloaded -= PreviewPane_Unloaded;
			GotFocus -= PreviewPane_GotFocus;
			PreviewSurface.LayoutUpdated -= PreviewSurface_LayoutUpdated;
			PreviewSurface.SizeChanged -= PreviewSurface_SizeChanged;
			UnsubscribePreviewRoot();
			UnregisterPropertyChangedCallback(VisibilityProperty, _visibilityChangedToken);
			SetSubscribedViewModel(null);
		}

		lock (_lifecycleLock)
		{
			_disposeTask ??= _cleanupTask ??= CleanupPreviewAsync();

			return new ValueTask(_disposeTask);
		}
	}

	internal void UpdateShellPreviewPointer(Point point)
	{
		if (_shellSession is null)
		{
			return;
		}

		SetPreviewHostCloaked(false);
	}

	private static void ViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
	{
		if (sender is not PreviewPane previewPane)
		{
			return;
		}

		var isActive = previewPane.IsLoaded && previewPane.Visibility is Visibility.Visible;
		previewPane.SetSubscribedViewModel(isActive ? args.NewValue as PreviewPaneViewModel : null);
		if (isActive)
		{
			previewPane.QueueRender();
		}
	}

	private async void PreviewPane_Loaded(object sender, RoutedEventArgs e)
	{
		try
		{
			await WaitForCleanupAsync();

			if (Volatile.Read(ref _isDisposed) is not 0 || !IsLoaded || Visibility is not Visibility.Visible)
			{
				return;
			}

			SubscribePreviewRoot();
			SetSubscribedViewModel(ViewModel);
			QueueRender();
		}
		catch (Exception exception)
		{
			System.Diagnostics.Debug.WriteLine($"Preview loading failed: {exception}");
		}
	}

	private async void PreviewPane_VisibilityChanged(DependencyObject sender, DependencyProperty property)
	{
		if (Volatile.Read(ref _isDisposed) is not 0 || !IsLoaded)
		{
			return;
		}

		try
		{
			if (Visibility is not Visibility.Visible)
			{
				SetSubscribedViewModel(null);
				await BeginCleanupAsync();

				return;
			}

			await WaitForCleanupAsync();
			if (Volatile.Read(ref _isDisposed) is not 0 || !IsLoaded || Visibility is not Visibility.Visible)
			{
				return;
			}

			SetSubscribedViewModel(ViewModel);
			QueueRender();
		}
		catch (Exception exception)
		{
			System.Diagnostics.Debug.WriteLine($"Preview visibility change failed: {exception}");
		}
	}

	private async void PreviewPane_Unloaded(object sender, RoutedEventArgs e)
	{
		UnsubscribePreviewRoot();
		SetSubscribedViewModel(null);
		try
		{
			await BeginCleanupAsync();
		}
		catch (Exception exception)
		{
			System.Diagnostics.Debug.WriteLine($"Preview unloading failed: {exception}");
		}
	}

	private void PreviewSurface_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		QueueShellLayoutUpdate();
	}

	private void PreviewRoot_PointerChanged(object sender, PointerRoutedEventArgs e)
	{
		UpdateShellPreviewPointer(e.GetCurrentPoint(PreviewSurface).Position);
	}

	private void PreviewSurface_PointerEntered(object sender, PointerRoutedEventArgs e)
	{
		SetPreviewHostCloaked(false);
		e.Handled = true;
	}

	private void SubscribePreviewRoot()
	{
		UnsubscribePreviewRoot();
		_subscribedXamlRoot = XamlRoot;
		if (_subscribedXamlRoot is not null)
		{
			_subscribedXamlRoot.Changed += PreviewXamlRoot_Changed;
			_inputRoot = _subscribedXamlRoot.Content;
			_inputRoot?.AddHandler(PointerEnteredEvent, _previewRootPointerChangedHandler, true);
			_inputRoot?.AddHandler(PointerMovedEvent, _previewRootPointerChangedHandler, true);
		}
	}

	private void UnsubscribePreviewRoot()
	{
		if (_subscribedXamlRoot is not null)
		{
			_subscribedXamlRoot.Changed -= PreviewXamlRoot_Changed;
			_subscribedXamlRoot = null;
		}

		if (_inputRoot is not null)
		{
			_inputRoot.RemoveHandler(PointerEnteredEvent, _previewRootPointerChangedHandler);
			_inputRoot.RemoveHandler(PointerMovedEvent, _previewRootPointerChangedHandler);
			_inputRoot = null;
		}
	}

	private void PreviewXamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
	{
		QueueShellLayoutUpdate();
	}

	private void PreviewSurface_LayoutUpdated(object? sender, object e)
	{
		QueueShellLayoutUpdate();
	}

	private async void PreviewPane_GotFocus(object sender, RoutedEventArgs e)
	{
		var session = _shellSession;
		if (session is null || Volatile.Read(ref _isDisposed) is not 0 || Volatile.Read(ref _isMovingFocusFromPreview) is not 0)
		{
			return;
		}

		try
		{
			SetPreviewHostCloaked(false);
			await session.SetFocusAsync();
		}
		catch (ObjectDisposedException)
		{
		}
		catch (Exception exception)
		{
			System.Diagnostics.Debug.WriteLine($"Preview focus transfer failed: {exception}");
		}
	}

	private async void PreviewAnywayButton_Click(object sender, RoutedEventArgs e)
	{
		if (ViewModel is not { } viewModel || !viewModel.CanPreviewUntrusted)
		{
			return;
		}

		PreviewAnywayButton.IsEnabled = false;
		try
		{
			await viewModel.PreviewUntrustedAsync();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			ShowStatus(Strings.PreviewFailed.GetLocalized(), isLoading: false);
			System.Diagnostics.Debug.WriteLine($"Untrusted preview retry failed: {exception}");
		}
		finally
		{
			PreviewAnywayButton.IsEnabled = true;
		}
	}

	private void SetSubscribedViewModel(PreviewPaneViewModel? value)
	{
		if (ReferenceEquals(_subscribedViewModel, value))
		{
			return;
		}

		if (_subscribedViewModel is not null)
		{
			_subscribedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
		}

		_subscribedViewModel = value;
		if (_subscribedViewModel is not null)
		{
			_subscribedViewModel.PropertyChanged += ViewModel_PropertyChanged;
		}
	}

	private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(PreviewPaneViewModel.Snapshot))
		{
			QueueRender();
		}
	}

	private void QueueRender()
	{
		if (Volatile.Read(ref _isDisposed) is not 0 || !IsLoaded || Visibility is not Visibility.Visible)
		{
			return;
		}

		lock (_lifecycleLock)
		{
			if (_cleanupTask is { IsCompleted: false })
			{
				return;
			}
		}

		var cancellation = new CancellationTokenSource();
		CancellationTokenSource? previousCancellation;
		lock (_lifecycleLock)
		{
			previousCancellation = _renderCancellation;
			_renderCancellation = cancellation;
		}

		previousCancellation?.Cancel();
		var version = Interlocked.Increment(ref _renderVersion);
		_ = RenderAsync(version, cancellation);
	}

	private Task RenderCurrentAsync()
	{
		QueueRender();

		return Task.CompletedTask;
	}

	private async Task RenderAsync(long version, CancellationTokenSource cancellationSource)
	{
		var cancellationToken = cancellationSource.Token;
		var entered = false;
		BrowsePreviewSnapshot? renderedSnapshot = null;
		try
		{
			await _renderGate.WaitAsync(cancellationToken);
			entered = true;
			cancellationToken.ThrowIfCancellationRequested();

			await DisposeShellSessionAsync(Interlocked.Exchange(ref _shellSession, null));
			DestroyPreviewHost();
			ClearRenderedContent();
			_waitingForShellLayout = false;

			if (!IsCurrentRender(version, cancellationToken))
			{
				return;
			}

			if (ViewModel is not { } viewModel)
			{
				ShowStatus(Strings.PreviewEmpty.GetLocalized(), isLoading: false);

				return;
			}

			var snapshot = viewModel.Snapshot;
			renderedSnapshot = snapshot;
			switch (snapshot.Status)
			{
				case BrowsePreviewStatus.Empty:
				case BrowsePreviewStatus.Loading:
				case BrowsePreviewStatus.Blocked:
				case BrowsePreviewStatus.Unavailable:
				case BrowsePreviewStatus.Failed:
					ShowStatus(viewModel.StatusText, snapshot.Status is BrowsePreviewStatus.Loading);

					return;
			}

			if (snapshot.Result is WindowsShellPreviewResult shellResult)
			{
				await RenderShellAsync(shellResult, version, cancellationToken);

				return;
			}

			ShowStatus(Strings.PreviewUnsupported.GetLocalized(), isLoading: false);
		}
		catch (WindowsShellPreviewBlockedException exception)
		{
			if (IsCurrentRender(version, cancellationToken))
			{
				await DisposeShellSessionAsync(Interlocked.Exchange(ref _shellSession, null));
				DestroyPreviewHost();
				if (!IsCurrentRender(version, cancellationToken))
				{
					return;
				}

				var reported = renderedSnapshot is not null && ViewModel is { } viewModel && viewModel.TryReportShellPreviewBlocked(renderedSnapshot, exception.Reason);
				if (reported)
				{
					ShowStatus(exception.Reason is PreviewBlockReason.Untrusted ? Strings.PreviewUntrusted.GetLocalized() : Strings.PreviewBlocked.GetLocalized(), isLoading: false);
				}

				System.Diagnostics.Debug.WriteLine($"Preview activation was blocked: {exception.Reason}");
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			if (IsCurrentRender(version, cancellationToken))
			{
				await DisposeShellSessionAsync(Interlocked.Exchange(ref _shellSession, null));
				DestroyPreviewHost();
				if (!IsCurrentRender(version, cancellationToken))
				{
					return;
				}

				ShowStatus(Strings.PreviewFailed.GetLocalized(), isLoading: false);
				System.Diagnostics.Debug.WriteLine($"Preview rendering failed: {exception}");
			}
		}
		finally
		{
			if (entered)
			{
				_renderGate.Release();
			}

			lock (_lifecycleLock)
			{
				if (ReferenceEquals(_renderCancellation, cancellationSource))
				{
					_renderCancellation = null;
				}
			}

			cancellationSource.Dispose();
		}
	}

	private async Task RenderShellAsync(WindowsShellPreviewResult result, long version, CancellationToken cancellationToken)
	{
		if (SessionFactory is null || _windowHandle.IsNull)
		{
			ShowStatus(Strings.PreviewUnavailable.GetLocalized(), isLoading: false);

			return;
		}

		if (!TryGetHostLayout(out var layout))
		{
			_waitingForShellLayout = true;
			ShowStatus(Strings.Loading.GetLocalized(), isLoading: true);

			return;
		}

		EnsurePreviewHost(layout);
		SetPreviewHostLayout(layout);
		var acceleratorSink = new PreviewAcceleratorSink(this, version);
		var host = new WindowsPreviewHost(_previewHost, new WindowsPreviewBounds(0, 0, layout.Width, layout.Height), acceleratorSink.TryForward);
		var session = await SessionFactory.CreateAsync(result, host, cancellationToken);
		if (!IsCurrentRender(version, cancellationToken))
		{
			await DisposeShellSessionAsync(session);

			return;
		}

		_shellSession = session;
		try
		{
			if (!TryGetHostLayout(out var currentLayout))
			{
				throw new InvalidOperationException("The preview surface is no longer available after handler activation.");
			}

			EnsurePreviewHostComposition(currentLayout);
			UpdatePreviewCompositionLayout(currentLayout);
			SetPreviewHostLayout(currentLayout);
			await session.SetBoundsAsync(new WindowsPreviewBounds(0, 0, currentLayout.Width, currentLayout.Height), cancellationToken);

			_appliedHostLayout = currentLayout;
			RefreshPreviewHost();
			UpdatePreviewHostInput();
		}
		catch
		{
			await DisposeShellSessionAsync(Interlocked.Exchange(ref _shellSession, null));

			throw;
		}
	}

	private void QueueShellLayoutUpdate()
	{
		if (_waitingForShellLayout && TryGetHostLayout(out _))
		{
			_waitingForShellLayout = false;
			QueueRender();

			return;
		}

		if (Volatile.Read(ref _isDisposed) is not 0 || !IsLoaded || _shellSession is null || _previewHost.IsNull)
		{
			return;
		}

		if (Interlocked.Exchange(ref _layoutUpdateQueued, 1) is not 0)
		{
			return;
		}

		if (!DispatcherQueue.TryEnqueue(() => _ = UpdateShellLayoutAsync()))
		{
			Interlocked.Exchange(ref _layoutUpdateQueued, 0);
		}
	}

	private async Task UpdateShellLayoutAsync()
	{
		var entered = false;
		var requiresFollowUp = false;
		try
		{
			await _renderGate.WaitAsync();
			entered = true;
			if (Volatile.Read(ref _isDisposed) is not 0 || !IsLoaded || _shellSession is not { } session || !TryGetHostLayout(out var layout) || _appliedHostLayout == layout)
			{
				return;
			}

			var previousLayout = _appliedHostLayout;
			SetPreviewHostLayout(layout);
			UpdatePreviewCompositionLayout(layout);
			if (previousLayout is null || previousLayout.Value.Width != layout.Width || previousLayout.Value.Height != layout.Height)
			{
				await session.SetBoundsAsync(new WindowsPreviewBounds(0, 0, layout.Width, layout.Height));
			}

			_appliedHostLayout = layout;
			UpdatePreviewHostInput();
			requiresFollowUp = TryGetHostLayout(out var latestLayout) && _appliedHostLayout != latestLayout;
		}
		catch (ObjectDisposedException)
		{
		}
		catch (Exception exception)
		{
			System.Diagnostics.Debug.WriteLine($"Preview layout update failed: {exception}");
		}
		finally
		{
			if (entered)
			{
				_renderGate.Release();
			}

			Interlocked.Exchange(ref _layoutUpdateQueued, 0);
			if (requiresFollowUp)
			{
				QueueShellLayoutUpdate();
			}
		}
	}

	private bool IsCurrentRender(long version, CancellationToken cancellationToken)
	{
		return !cancellationToken.IsCancellationRequested && version == Volatile.Read(ref _renderVersion) && Volatile.Read(ref _isDisposed) is 0 && IsLoaded && Visibility is Visibility.Visible;
	}

	private void ClearRenderedContent()
	{
		StatusPanel.Visibility = Visibility.Collapsed;
		LoadingIndicator.IsActive = false;
		PreviewAnywayButton.Visibility = Visibility.Collapsed;
	}

	private void ShowStatus(string text, bool isLoading)
	{
		StatusTextBlock.Text = text;
		LoadingIndicator.IsActive = isLoading;
		LoadingIndicator.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
		PreviewAnywayButton.Visibility = ViewModel?.CanPreviewUntrusted is true ? Visibility.Visible : Visibility.Collapsed;
		StatusPanel.Visibility = Visibility.Visible;
	}

	private static LRESULT PreviewHostWindowProc(HWND windowHandle, uint message, WPARAM wParam, LPARAM lParam)
	{
		return PInvoke.DefWindowProc(windowHandle, message, wParam, lParam);
	}

	private unsafe void EnsurePreviewHost(PreviewHostLayout layout)
	{
		if (_windowHandle.IsNull || !PInvoke.IsWindow(_windowHandle))
		{
			throw new InvalidOperationException("The preview host owner window is not valid.");
		}

		if (!_previewHost.IsNull)
		{
			if (PInvoke.IsWindow(_previewHost))
			{
				return;
			}

			_previewHost = HWND.Null;
		}

		EnsurePreviewHostClass();
		fixed (char* className = _previewHostClassName)
		{
			_previewHost = PInvoke.CreateWindowEx(
				WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_COMPOSITED,
				className,
				default,
				WINDOW_STYLE.WS_CHILD | WINDOW_STYLE.WS_CLIPSIBLINGS | WINDOW_STYLE.WS_VISIBLE,
				layout.X, layout.Y, layout.Width, layout.Height, _windowHandle, HMENU.Null, _previewHostWindowInstance, null);
		}

		if (_previewHost.IsNull)
		{
			var error = Marshal.GetLastPInvokeError();
			DestroyPreviewHostClass();

			throw new InvalidOperationException($"The preview host window could not be created. Win32 error {error}.");
		}
	}

	private unsafe void EnsurePreviewHostClass()
	{
		if (_previewHostWindowProc is not null)
		{
			return;
		}

		_previewHostWindowProc = new WNDPROC(PreviewHostWindowProc);
		WNDCLASSEXW windowClass = default;
		windowClass.cbSize = (uint)sizeof(WNDCLASSEXW);
		windowClass.hInstance = PInvoke.GetModuleHandle(default(PCWSTR));
		windowClass.lpfnWndProc = (delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, LRESULT>)Marshal.GetFunctionPointerForDelegate(_previewHostWindowProc);
		_previewHostWindowInstance = windowClass.hInstance;
		fixed (char* className = _previewHostClassName)
		{
			windowClass.lpszClassName = className;
			if (PInvoke.RegisterClassEx(&windowClass) == 0)
			{
				var error = Marshal.GetLastPInvokeError();
				_previewHostWindowProc = null;
				_previewHostWindowInstance = HINSTANCE.Null;

				throw new InvalidOperationException($"The preview host window class could not be registered. Win32 error {error}.");
			}
		}
	}

	private void DestroyPreviewHost()
	{
		DestroyPreviewHostComposition();

		if (_previewHost.IsNull)
		{
			DestroyPreviewHostClass();

			return;
		}

		if (PInvoke.IsWindow(_previewHost))
		{
			if (!PInvoke.DestroyWindow(_previewHost))
			{
				var error = Marshal.GetLastPInvokeError();
				System.Diagnostics.Debug.WriteLine($"Preview host destruction failed with Win32 error {error}.");
				if (PInvoke.IsWindow(_previewHost))
				{
					return;
				}
			}
		}

		_previewHost = HWND.Null;
		_isPreviewHostCloaked = false;
		_appliedHostLayout = null;
		DestroyPreviewHostClass();
	}

	private unsafe void DestroyPreviewHostClass()
	{
		if (_previewHostWindowProc is null)
		{
			return;
		}

		fixed (char* className = _previewHostClassName)
		{
			if (!PInvoke.UnregisterClass(className, _previewHostWindowInstance))
			{
				System.Diagnostics.Debug.WriteLine($"Preview host window class destruction failed with Win32 error {Marshal.GetLastPInvokeError()}.");

				return;
			}
		}

		_previewHostWindowProc = null;
		_previewHostWindowInstance = HINSTANCE.Null;
	}

	private unsafe void EnsurePreviewHostComposition(PreviewHostLayout layout)
	{
		if (_contentExternalOutputLink is not null)
		{
			return;
		}

		if (_previewHost.IsNull || PreviewSurface.XamlRoot is not { } xamlRoot)
		{
			throw new InvalidOperationException("The preview host is not ready for composition.");
		}

		D3D_DRIVER_TYPE[] driverTypes = [D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP];
		HRESULT hr = HRESULT.E_FAIL;
		ID3D11Device? d3d11Device = null;
		ID3D11DeviceContext? d3d11DeviceContext = null;
		foreach (var driverType in driverTypes)
		{
			hr = PInvoke.D3D11CreateDevice(
				null!, driverType, new(nint.Zero), D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
				ReadOnlySpan<D3D_FEATURE_LEVEL>.Empty, 7, out d3d11Device, out d3d11DeviceContext);
			if (hr.Succeeded)
			{
				break;
			}
		}

		if (hr.Failed || d3d11Device is null || d3d11DeviceContext is null)
		{
			throw new COMException("The preview composition device could not be created.", hr.Value);
		}

		_d3d11Device = d3d11Device;
		_d3d11DeviceContext = d3d11DeviceContext;
		try
		{
			var dxgiDevice = (IDXGIDevice)d3d11Device;
			hr = PInvoke.DCompositionCreateDevice<IDCompositionDevice>(dxgiDevice, out var compositionDevice);
			hr.ThrowOnFailure();
			_compositionDevice = compositionDevice;
			hr = compositionDevice.CreateVisual(out var previewVisual);
			hr.ThrowOnFailure();
			_previewVisual = previewVisual;
			hr = compositionDevice.CreateSurfaceFromHwnd(_previewHost, out var previewSurface);
			hr.ThrowOnFailure();
			_previewSurface = previewSurface;
			hr = previewVisual.SetContent(previewSurface);
			hr.ThrowOnFailure();

			var compositor = ElementCompositionPreview.GetElementVisual(PreviewSurface).Compositor;
			_contentExternalOutputLink = ContentExternalOutputLink.Create(compositor);
			var target = WinRT.CastExtensions.As<IDCompositionTarget>(_contentExternalOutputLink);
			hr = target.SetRoot(previewVisual);
			hr.ThrowOnFailure();
			_contentExternalOutputLink.PlacementVisual.Scale = new Vector3(1 / (float)layout.Scale);
			_contentExternalOutputLink.PlacementVisual.Size = new Vector2(layout.Width, layout.Height);
			ElementCompositionPreview.SetElementChildVisual(PreviewSurface, _contentExternalOutputLink.PlacementVisual);
			hr = compositionDevice.Commit();
			hr.ThrowOnFailure();
			SetPreviewHostCloaked(false);
		}
		catch
		{
			DestroyPreviewHostComposition();

			throw;
		}
	}

	private void UpdatePreviewCompositionLayout(PreviewHostLayout layout)
	{
		if (_contentExternalOutputLink is not null)
		{
			_contentExternalOutputLink.PlacementVisual.Scale = new Vector3(1 / (float)layout.Scale);
			_contentExternalOutputLink.PlacementVisual.Size = new Vector2(layout.Width, layout.Height);
		}
	}

	private void DestroyPreviewHostComposition()
	{
		if (_contentExternalOutputLink is not null)
		{
			try
			{
				ElementCompositionPreview.SetElementChildVisual(PreviewSurface, null!);
			}
			catch (Exception exception)
			{
				System.Diagnostics.Debug.WriteLine($"Preview composition detachment failed: {exception}");
			}

			_contentExternalOutputLink.Dispose();
			_contentExternalOutputLink = null;
		}

		_previewSurface = null;
		_previewVisual = null;
		_compositionDevice = null;
		_d3d11DeviceContext = null;
		_d3d11Device = null;
	}

	private unsafe void SetPreviewHostCloaked(bool cloaked)
	{
		if (_previewHost.IsNull || _isPreviewHostCloaked == cloaked)
		{
			return;
		}

		var value = cloaked ? 1u : 0u;
		var hr = PInvoke.DwmSetWindowAttribute(_previewHost, DWMWINDOWATTRIBUTE.DWMWA_CLOAK, &value, sizeof(uint));
		if (hr.Failed)
		{
			System.Diagnostics.Debug.WriteLine($"Preview host cloak update failed: 0x{hr.Value:X8}");

			return;
		}

		_isPreviewHostCloaked = cloaked;
		if (!cloaked && _appliedHostLayout is { } layout)
		{
			SetPreviewHostLayout(layout);
		}
	}

	private void UpdatePreviewHostInput()
	{
		if (_shellSession is null || _previewHost.IsNull || !PInvoke.GetCursorPos(out var cursor) || !PInvoke.GetWindowRect(_previewHost, out var bounds))
		{
			return;
		}

		SetPreviewHostCloaked(false);
	}

	private unsafe void RefreshPreviewHost()
	{
		// Invalidate the handler's children after attaching their redirected surface to XAML.
		_ = PInvoke.RedrawWindow(_previewHost, null, default, REDRAW_WINDOW_FLAGS.RDW_INVALIDATE | REDRAW_WINDOW_FLAGS.RDW_ERASE | REDRAW_WINDOW_FLAGS.RDW_ALLCHILDREN | REDRAW_WINDOW_FLAGS.RDW_UPDATENOW);
		_compositionDevice?.Commit().ThrowOnFailure();
	}

	private bool TryQueuePreviewAccelerator(long renderVersion, in MSG accelerator)
	{
		if (renderVersion != Volatile.Read(ref _renderVersion) || Volatile.Read(ref _isDisposed) is not 0)
		{
			return false;
		}

		try
		{
			var messageCopy = accelerator;

			return _dispatcherQueue.TryEnqueue(() => HandleForwardedPreviewAccelerator(renderVersion, messageCopy));
		}
		catch (Exception exception)
		{
			System.Diagnostics.Debug.WriteLine($"Preview keyboard bridge failed: {exception}");

			return false;
		}
	}

	private void HandleForwardedPreviewAccelerator(long renderVersion, MSG accelerator)
	{
		if (renderVersion != Volatile.Read(ref _renderVersion) || Volatile.Read(ref _isDisposed) is not 0 || !IsLoaded || Visibility is not Visibility.Visible)
		{
			return;
		}

		var virtualKey = unchecked((uint)accelerator.wParam.Value);
		var isControlDown = PInvoke.GetKeyState(VirtualKeyControl) < 0;
		var isAltDown = PInvoke.GetKeyState(VirtualKeyMenu) < 0;
		var isShiftDown = PInvoke.GetKeyState(VirtualKeyShift) < 0;
		if (!IsSupportedForwardedPreviewAccelerator(accelerator.message, virtualKey, isControlDown, isAltDown, isShiftDown))
		{
			return;
		}

		var session = _shellSession;
		var hostWindow = _previewHost;
		if (session is null || hostWindow.IsNull || accelerator.hwnd != hostWindow)
		{
			return;
		}

		if (!IsPreviewFocusCycler(accelerator.message, virtualKey, isControlDown))
		{
			ForwardPreviewAcceleratorToApplication(accelerator);

			return;
		}

		var request = new PreviewAcceleratorRequest(renderVersion, session, hostWindow, isShiftDown);
		if (_isForwardingPreviewAccelerator)
		{
			if (_pendingPreviewAccelerators.Count < PreviewAcceleratorQueueLimit)
			{
				_pendingPreviewAccelerators.Enqueue(request);
			}

			return;
		}

		_ = DrainPreviewAcceleratorsAsync(request);
	}

	private async Task DrainPreviewAcceleratorsAsync(PreviewAcceleratorRequest request)
	{
		_isForwardingPreviewAccelerator = true;
		try
		{
			var current = request;
			while (true)
			{
				await ForwardPreviewAcceleratorAsync(current);
				if (_pendingPreviewAccelerators.Count is 0)
				{
					break;
				}

				current = _pendingPreviewAccelerators.Dequeue();
			}
		}
		catch (Exception exception)
		{
			_pendingPreviewAccelerators.Clear();
			System.Diagnostics.Debug.WriteLine($"Preview accelerator processing failed: {exception}");
		}
		finally
		{
			_isForwardingPreviewAccelerator = false;
		}
	}

	private async Task ForwardPreviewAcceleratorAsync(PreviewAcceleratorRequest request)
	{
		if (!IsCurrentPreviewAccelerator(request))
		{
			return;
		}

		HWND focusedWindow;
		using var timeoutCancellation = new CancellationTokenSource(_previewFocusQueryTimeout);
		Task<HWND>? focusTask = null;
		try
		{
			focusTask = request.Session.QueryFocusAsync(timeoutCancellation.Token).AsTask();
			focusedWindow = await focusTask.WaitAsync(timeoutCancellation.Token);
		}
		catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
		{
			if (focusTask is not null)
			{
				_ = ObserveFocusQueryCompletionAsync(focusTask);
			}

			System.Diagnostics.Debug.WriteLine("Preview focus query timed out.");

			return;
		}
		catch (ObjectDisposedException)
		{
			return;
		}
		catch (Exception exception)
		{
			System.Diagnostics.Debug.WriteLine($"Preview focus query failed: {exception}");

			return;
		}

		var hasPreviewFocus = !focusedWindow.IsNull && (focusedWindow == request.HostWindow || PInvoke.IsChild(request.HostWindow, focusedWindow).Value is not 0);
		if (!IsCurrentPreviewAccelerator(request) || !hasPreviewFocus)
		{
			return;
		}

		MoveFocusFromPreview(request.IsShiftDown);
	}

	private bool IsCurrentPreviewAccelerator(PreviewAcceleratorRequest request)
	{
		var matchesSession = request.RenderVersion == Volatile.Read(ref _renderVersion) && ReferenceEquals(request.Session, _shellSession) && request.HostWindow == _previewHost;

		return matchesSession && Volatile.Read(ref _isDisposed) is 0 && IsLoaded && Visibility is Visibility.Visible;
	}

	private void ForwardPreviewAcceleratorToApplication(MSG accelerator)
	{
		if (_windowHandle.IsNull || !PInvoke.IsWindow(_windowHandle))
		{
			return;
		}

		if (PInvoke.PostMessage(_windowHandle, accelerator.message, accelerator.wParam, accelerator.lParam).Value is 0)
		{
			var error = Marshal.GetLastPInvokeError();
			System.Diagnostics.Debug.WriteLine($"Preview accelerator forwarding failed with Win32 error {error}.");
		}
	}

	private static async Task ObserveFocusQueryCompletionAsync(Task<HWND> focusTask)
	{
		try
		{
			await focusTask.ConfigureAwait(false);
		}
		catch
		{
		}
	}

	private void MoveFocusFromPreview(bool reverse)
	{
		Interlocked.Exchange(ref _isMovingFocusFromPreview, 1);
		try
		{
			Focus(FocusState.Keyboard);
			FocusManager.TryMoveFocus(reverse ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next);
		}
		finally
		{
			Interlocked.Exchange(ref _isMovingFocusFromPreview, 0);
		}
	}

	internal static bool IsSupportedForwardedPreviewAccelerator(uint message, uint virtualKey, bool isControlDown, bool isAltDown, bool isShiftDown)
	{
		if (virtualKey is VirtualKeyTab or VirtualKeyF6)
		{
			return message is WindowMessageKeyDown && !isControlDown && !isAltDown;
		}

		if (virtualKey is >= VirtualKeyF1 and <= VirtualKeyF12)
		{
			return message is WindowMessageKeyDown && !isControlDown && !isAltDown && !isShiftDown;
		}

		if (virtualKey is < VirtualKeyA or > VirtualKeyZ || isShiftDown)
		{
			return false;
		}

		return (message is WindowMessageKeyDown && isControlDown && !isAltDown) || (message is WindowMessageSystemKeyDown && !isControlDown && isAltDown);
	}

	internal static bool IsPreviewFocusCycler(uint message, uint virtualKey, bool isControlDown)
	{
		return message is WindowMessageKeyDown && !isControlDown && virtualKey is VirtualKeyTab or VirtualKeyF6;
	}

	private void SetPreviewHostLayout(PreviewHostLayout layout)
	{
		if (_previewHost.IsNull)
		{
			return;
		}

		var flags = SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW;
		if (!PInvoke.SetWindowPos(_previewHost, HWND.Null, layout.X, layout.Y, layout.Width, layout.Height, flags))
		{
			throw new InvalidOperationException($"The preview host window could not be positioned. Win32 error {Marshal.GetLastPInvokeError()}.");
		}
	}

	private bool TryGetHostLayout(out PreviewHostLayout layout)
	{
		layout = default;
		if (_windowHandle.IsNull || !PInvoke.IsWindow(_windowHandle) || PreviewSurface.XamlRoot is not { } xamlRoot || xamlRoot.Content is not UIElement rootElement || PreviewSurface.ActualWidth <= 0
			|| PreviewSurface.ActualHeight <= 0)
		{
			return false;
		}

		var point = PreviewSurface.TransformToVisual(rootElement).TransformPoint(new Point());
		var scale = xamlRoot.RasterizationScale;
		var width = Math.Max(1, (int)Math.Round(PreviewSurface.ActualWidth * scale));
		var height = Math.Max(1, (int)Math.Round(PreviewSurface.ActualHeight * scale));
		layout = new PreviewHostLayout((int)Math.Round(point.X * scale), (int)Math.Round(point.Y * scale), width, height, scale);

		return true;
	}

	private static async Task DisposeShellSessionAsync(IWindowsShellPreviewSession? session)
	{
		if (session is null)
		{
			return;
		}

		try
		{
			await session.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			System.Diagnostics.Debug.WriteLine($"Preview session disposal failed: {exception}");
		}
	}

	private Task BeginCleanupAsync()
	{
		lock (_lifecycleLock)
		{
			return _cleanupTask ??= CleanupPreviewAsync();
		}
	}

	private async Task WaitForCleanupAsync()
	{
		Task? cleanupTask;
		lock (_lifecycleLock)
		{
			cleanupTask = _cleanupTask;
		}

		if (cleanupTask is null)
		{
			return;
		}

		await cleanupTask;
		lock (_lifecycleLock)
		{
			if (_disposeTask is null && ReferenceEquals(_cleanupTask, cleanupTask))
			{
				_cleanupTask = null;
			}
		}
	}

	private async Task CleanupPreviewAsync()
	{
		_waitingForShellLayout = false;
		CancellationTokenSource? cancellation;
		lock (_lifecycleLock)
		{
			cancellation = _renderCancellation;
			_renderCancellation = null;
		}

		cancellation?.Cancel();
		var entered = false;
		try
		{
			await _renderGate.WaitAsync();
			entered = true;
			await DisposeShellSessionAsync(Interlocked.Exchange(ref _shellSession, null));
			DestroyPreviewHost();
		}
		finally
		{
			if (entered)
			{
				_renderGate.Release();
			}
		}
	}

	private sealed class PreviewAcceleratorSink
	{
		private readonly WeakReference<PreviewPane> _owner;
		private readonly long _renderVersion;

		public PreviewAcceleratorSink(PreviewPane owner, long renderVersion)
		{
			_owner = new WeakReference<PreviewPane>(owner);
			_renderVersion = renderVersion;
		}

		public bool TryForward(in MSG accelerator)
		{
			return _owner.TryGetTarget(out var owner) && owner.TryQueuePreviewAccelerator(_renderVersion, in accelerator);
		}
	}

	private readonly record struct PreviewAcceleratorRequest(long RenderVersion, IWindowsShellPreviewSession Session, HWND HostWindow, bool IsShiftDown);

	private readonly record struct PreviewHostLayout(int X, int Y, int Width, int Height, double Scale);
}

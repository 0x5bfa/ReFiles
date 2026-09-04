// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Files.Core.Browsing;
using Files.Core.Capabilities.Previews;
using Files.Localization;
using Files.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT.Interop;

namespace Files.Views;

public sealed partial class PreviewPane : UserControl, IDisposable, IAsyncDisposable
{
	private const int TextPreviewByteLimit = 256 * 1024;
	private const int ImagePreviewByteLimit = 32 * 1024 * 1024;
	private const ulong ImagePreviewPixelLimit = 100_000_000;
	private const int ImagePreviewDecodeDimensionLimit = 4096;
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

	private PreviewPaneViewModel? _subscribedViewModel;
	private IWindowsShellPreviewSession? _shellSession;
	private CancellationTokenSource? _renderCancellation;
	private Task? _cleanupTask;
	private Task? _disposeTask;

	private HWND _previewHost;
	private HWND _windowHandle;
	private PreviewHostLayout? _appliedHostLayout;
	private long _renderVersion;
	private int _layoutUpdateQueued;
	private int _isMovingFocusFromPreview;
	private int _isDisposed;
	private bool _isForwardingPreviewAccelerator;

	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.Register(nameof(ViewModel), typeof(PreviewPaneViewModel), typeof(PreviewPane), new PropertyMetadata(null, ViewModelChanged));

	public PreviewPaneViewModel? ViewModel
	{
		get => (PreviewPaneViewModel?)GetValue(ViewModelProperty);
		set => SetValue(ViewModelProperty, value);
	}

	public IWindowsShellPreviewSessionFactory? SessionFactory { get; set; }

	public PreviewPane()
	{
		InitializeComponent();
		_dispatcherQueue = DispatcherQueue;
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
			UnregisterPropertyChangedCallback(VisibilityProperty, _visibilityChangedToken);
			SetSubscribedViewModel(null);
		}

		lock (_lifecycleLock)
		{
			_disposeTask ??= _cleanupTask ??= CleanupPreviewAsync();

			return new ValueTask(_disposeTask);
		}
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

			if (snapshot.Result is StreamPreviewResult streamResult)
			{
				await RenderStreamAsync(streamResult, version, cancellationToken);

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

	private async Task RenderStreamAsync(StreamPreviewResult result, long version, CancellationToken cancellationToken)
	{
		using var contentLease = result.AcquireContent();
		var content = contentLease.Content;
		if (IsImageContentType(result.ContentType))
		{
			if (content.CanSeek)
			{
				content.Position = 0;
			}

			var encodedImage = await ReadImageAsync(content, result.ContentLength, cancellationToken);
			var dimensions = await ReadImageDimensionsAsync(encodedImage, cancellationToken);
			if (dimensions.Width is 0 || dimensions.Height is 0 || (ulong)dimensions.Width * dimensions.Height > ImagePreviewPixelLimit)
			{
				throw new InvalidDataException("The preview image dimensions exceed the safe decoding limit.");
			}

			var bitmap = new BitmapImage();
			if (dimensions.Width > ImagePreviewDecodeDimensionLimit || dimensions.Height > ImagePreviewDecodeDimensionLimit)
			{
				if (dimensions.Width >= dimensions.Height)
				{
					bitmap.DecodePixelWidth = ImagePreviewDecodeDimensionLimit;
				}
				else
				{
					bitmap.DecodePixelHeight = ImagePreviewDecodeDimensionLimit;
				}
			}

			using var imageStream = new MemoryStream(encodedImage, writable: false);
			using var randomAccessStream = imageStream.AsRandomAccessStream();
			await bitmap.SetSourceAsync(randomAccessStream);
			cancellationToken.ThrowIfCancellationRequested();

			if (!IsCurrentRender(version, cancellationToken))
			{
				return;
			}

			PreviewImage.Source = bitmap;
			PreviewImage.Visibility = Visibility.Visible;

			return;
		}

		if (IsTextContentType(result.ContentType))
		{
			if (content.CanSeek)
			{
				content.Position = 0;
			}

			var text = await ReadTextAsync(content, cancellationToken);
			if (!IsCurrentRender(version, cancellationToken))
			{
				return;
			}

			PreviewTextBlock.Text = text;
			PreviewTextScroller.Visibility = Visibility.Visible;

			return;
		}

		ShowStatus(Strings.PreviewUnsupported.GetLocalized(), isLoading: false);
	}

	private async Task RenderShellAsync(WindowsShellPreviewResult result, long version, CancellationToken cancellationToken)
	{
		if (SessionFactory is null || _windowHandle.IsNull || !TryGetHostLayout(out var layout))
		{
			ShowStatus(Strings.PreviewUnavailable.GetLocalized(), isLoading: false);

			return;
		}

		EnsurePreviewHost();
		SetPreviewHostLayout(layout, show: false);
		var acceleratorSink = new PreviewAcceleratorSink(this, version);
		var host = new WindowsPreviewHost(_previewHost, new WindowsPreviewBounds(0, 0, layout.Width, layout.Height), acceleratorSink.TryForward);
		var session = await SessionFactory.CreateAsync(result, host, cancellationToken);
		if (!IsCurrentRender(version, cancellationToken))
		{
			await DisposeShellSessionAsync(session);

			return;
		}

		_shellSession = session;
		SetPreviewHostLayout(layout, show: true);
		_appliedHostLayout = layout;
	}

	private void QueueShellLayoutUpdate()
	{
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
			SetPreviewHostLayout(layout, show: true);
			if (previousLayout is null || previousLayout.Value.Width != layout.Width || previousLayout.Value.Height != layout.Height)
			{
				await session.SetBoundsAsync(new WindowsPreviewBounds(0, 0, layout.Width, layout.Height));
			}

			_appliedHostLayout = layout;
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
		PreviewImage.Source = null;
		PreviewImage.Visibility = Visibility.Collapsed;
		PreviewTextBlock.Text = string.Empty;
		PreviewTextScroller.Visibility = Visibility.Collapsed;
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

	private unsafe void EnsurePreviewHost()
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

		_previewHost = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_NOPARENTNOTIFY, "STATIC", null, WINDOW_STYLE.WS_CHILD | WINDOW_STYLE.WS_CLIPCHILDREN, 0, 0, 1, 1, _windowHandle, null!, null!, null);

		if (_previewHost.IsNull)
		{
			throw new InvalidOperationException($"The preview host window could not be created. Win32 error {Marshal.GetLastPInvokeError()}.");
		}
	}

	private void DestroyPreviewHost()
	{
		if (_previewHost.IsNull)
		{
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
		_appliedHostLayout = null;
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

	private void SetPreviewHostLayout(PreviewHostLayout layout, bool show)
	{
		if (_previewHost.IsNull)
		{
			return;
		}

		var flags = SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER;
		flags |= show ? SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW : SET_WINDOW_POS_FLAGS.SWP_HIDEWINDOW;
		if (!PInvoke.SetWindowPos(_previewHost, HWND.Null, layout.X, layout.Y, layout.Width, layout.Height, flags))
		{
			throw new InvalidOperationException($"The preview host window could not be positioned. Win32 error {Marshal.GetLastPInvokeError()}.");
		}
	}

	private bool TryGetHostLayout(out PreviewHostLayout layout)
	{
		layout = default;
		if (_windowHandle.IsNull || !PInvoke.IsWindow(_windowHandle) || PreviewSurface.XamlRoot is not { } xamlRoot || PreviewSurface.ActualWidth <= 0 || PreviewSurface.ActualHeight <= 0)
		{
			return false;
		}

		var point = PreviewSurface.TransformToVisual(null).TransformPoint(new Point());
		var scale = xamlRoot.RasterizationScale;
		var width = Math.Max(1, (int)Math.Round(PreviewSurface.ActualWidth * scale));
		var height = Math.Max(1, (int)Math.Round(PreviewSurface.ActualHeight * scale));
		layout = new PreviewHostLayout((int)Math.Round(point.X * scale), (int)Math.Round(point.Y * scale), width, height);

		return true;
	}

	private static bool IsImageContentType(string contentType) =>
		contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
		&& !contentType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase);

	private static bool IsTextContentType(string contentType) =>
		contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
		|| contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
		|| contentType.Equals("application/xml", StringComparison.OrdinalIgnoreCase);

	private static async Task<string> ReadTextAsync(Stream stream, CancellationToken cancellationToken)
	{
		var buffer = new byte[TextPreviewByteLimit];
		var length = 0;
		while (length < buffer.Length)
		{
			var read = await stream.ReadAsync(buffer.AsMemory(length, buffer.Length - length), cancellationToken);
			if (read is 0)
			{
				break;
			}

			length += read;
		}

		var text = Encoding.UTF8.GetString(buffer, 0, length);
		if (length == buffer.Length)
		{
			text += Environment.NewLine + "...";
		}

		return text;
	}

	private static async Task<byte[]> ReadImageAsync(Stream stream, long? contentLength, CancellationToken cancellationToken)
	{
		if (contentLength > ImagePreviewByteLimit)
		{
			throw new InvalidDataException("The encoded preview image exceeds the safe size limit.");
		}

		using var output = new MemoryStream();
		var buffer = new byte[81920];
		var length = 0;
		while (true)
		{
			var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, ImagePreviewByteLimit - length + 1)), cancellationToken);
			if (read is 0)
			{
				break;
			}

			length += read;
			if (length > ImagePreviewByteLimit)
			{
				throw new InvalidDataException("The encoded preview image exceeds the safe size limit.");
			}

			output.Write(buffer, 0, read);
		}

		return output.ToArray();
	}

	private static async Task<(uint Width, uint Height)> ReadImageDimensionsAsync(byte[] encodedImage, CancellationToken cancellationToken)
	{
		using var imageStream = new MemoryStream(encodedImage, writable: false);
		using var randomAccessStream = imageStream.AsRandomAccessStream();
		var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
		cancellationToken.ThrowIfCancellationRequested();

		return (decoder.OrientedPixelWidth, decoder.OrientedPixelHeight);
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

	private readonly record struct PreviewHostLayout(int X, int Y, int Width, int Height);
}

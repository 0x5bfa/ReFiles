// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Files.Core.Browsing;
using Files.Core.Capabilities.Previews;
using Files.Localization;
using Files.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT.Interop;

namespace Files.Views;

public sealed partial class PreviewPane : UserControl, IDisposable, IAsyncDisposable
{
	private const int TextPreviewByteLimit = 256 * 1024;

	private readonly SemaphoreSlim _renderGate = new(1, 1);
	private readonly Lock _lifecycleLock = new();

	private PreviewPaneViewModel? _subscribedViewModel;
	private IWindowsShellPreviewSession? _shellSession;
	private CancellationTokenSource? _renderCancellation;
	private Task? _cleanupTask;
	private Task? _disposeTask;

	private HWND _previewHost;
	private nint _windowHandle;
	private long _renderVersion;
	private int _isDisposed;

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
		PreviewTitleBlock.Text = Strings.Preview.GetLocalized();
		Loaded += PreviewPane_Loaded;
		Unloaded += PreviewPane_Unloaded;
	}

	public void AttachWindow(Window window)
	{
		ArgumentNullException.ThrowIfNull(window);
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) is not 0, this);

		_windowHandle = WindowNative.GetWindowHandle(window);
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
			PreviewSurface.SizeChanged -= PreviewSurface_SizeChanged;
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

		previewPane.SetSubscribedViewModel(previewPane.IsLoaded ? args.NewValue as PreviewPaneViewModel : null);
		if (previewPane.IsLoaded)
		{
			previewPane.QueueRender();
		}
	}

	private async void PreviewPane_Loaded(object sender, RoutedEventArgs e)
	{
		try
		{
			Task? cleanupTask;
			lock (_lifecycleLock)
			{
				cleanupTask = _cleanupTask;
			}

			if (cleanupTask is not null)
			{
				await cleanupTask;
				lock (_lifecycleLock)
				{
					if (_disposeTask is null && ReferenceEquals(_cleanupTask, cleanupTask))
					{
						_cleanupTask = null;
					}
				}
			}

			if (Volatile.Read(ref _isDisposed) is not 0 || !IsLoaded)
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
		if (Volatile.Read(ref _isDisposed) is not 0)
		{
			return;
		}

		if (IsLoaded && ViewModel?.ShellResult is not null)
		{
			QueueRender();
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
		if (e.PropertyName is nameof(PreviewPaneViewModel.Snapshot)
			or nameof(PreviewPaneViewModel.Status)
			or nameof(PreviewPaneViewModel.Result)
			or nameof(PreviewPaneViewModel.StreamResult)
			or nameof(PreviewPaneViewModel.ShellResult))
		{
			QueueRender();
		}
	}

	private void QueueRender()
	{
		if (Volatile.Read(ref _isDisposed) is not 0 || !IsLoaded)
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
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			if (IsCurrentRender(version, cancellationToken))
			{
				DestroyPreviewHost();
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
		if (IsImageContentType(result.ContentType))
		{
			if (result.Content.CanSeek)
			{
				result.Content.Position = 0;
			}

			var bitmap = new BitmapImage();
			await bitmap.SetSourceAsync(result.Content.AsRandomAccessStream());
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
			if (result.Content.CanSeek)
			{
				result.Content.Position = 0;
			}

			var text = await ReadTextAsync(result.Content, cancellationToken);
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
		if (SessionFactory is null || _windowHandle is 0 || !TryGetHostLayout(out var layout))
		{
			ShowStatus(Strings.PreviewUnavailable.GetLocalized(), isLoading: false);

			return;
		}

		EnsurePreviewHost();
		SetPreviewHostLayout(layout, show: true);
		var host = new WindowsPreviewHost((nint)_previewHost, new WindowsPreviewBounds(0, 0, layout.Width, layout.Height));
		var session = await SessionFactory.CreateAsync(result, host, cancellationToken);
		if (!IsCurrentRender(version, cancellationToken))
		{
			await DisposeShellSessionAsync(session);

			return;
		}

		_shellSession = session;
	}

	private bool IsCurrentRender(long version, CancellationToken cancellationToken)
	{
		return !cancellationToken.IsCancellationRequested
			&& version == Volatile.Read(ref _renderVersion)
			&& Volatile.Read(ref _isDisposed) is 0
			&& IsLoaded;
	}

	private void ClearRenderedContent()
	{
		StatusPanel.Visibility = Visibility.Collapsed;
		LoadingIndicator.IsActive = false;
		PreviewImage.Source = null;
		PreviewImage.Visibility = Visibility.Collapsed;
		PreviewTextBlock.Text = string.Empty;
		PreviewTextScroller.Visibility = Visibility.Collapsed;
	}

	private void ShowStatus(string text, bool isLoading)
	{
		StatusTextBlock.Text = text;
		LoadingIndicator.IsActive = isLoading;
		LoadingIndicator.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
		StatusPanel.Visibility = Visibility.Visible;
	}

	private unsafe void EnsurePreviewHost()
	{
		if (_windowHandle is 0 || !PInvoke.IsWindow((HWND)_windowHandle))
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

		fixed (char* className = "STATIC")
		{
			_previewHost = PInvoke.CreateWindowEx(
				WINDOW_EX_STYLE.WS_EX_NOACTIVATE,
				className,
				default,
				WINDOW_STYLE.WS_CHILD | WINDOW_STYLE.WS_VISIBLE,
				0,
				0,
				1,
				1,
				(HWND)_windowHandle,
				HMENU.Null,
				HINSTANCE.Null,
				null);
		}

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
			PInvoke.DestroyWindow(_previewHost);
		}

		_previewHost = HWND.Null;
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
		if (_windowHandle is 0 || !PInvoke.IsWindow((HWND)_windowHandle) || PreviewSurface.XamlRoot is not { } xamlRoot || PreviewSurface.ActualWidth <= 0 || PreviewSurface.ActualHeight <= 0)
		{
			return false;
		}

		var point = PreviewSurface.TransformToVisual(null).TransformPoint(new Point());
		var scale = xamlRoot.RasterizationScale;
		var width = Math.Max(1, (int)Math.Round(PreviewSurface.ActualWidth * scale));
		var height = Math.Max(1, (int)Math.Round(PreviewSurface.ActualHeight * scale));
		layout = new PreviewHostLayout(
			(int)Math.Round(point.X * scale),
			(int)Math.Round(point.Y * scale),
			width,
			height);

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

	private readonly record struct PreviewHostLayout(int X, int Y, int Width, int Height);
}

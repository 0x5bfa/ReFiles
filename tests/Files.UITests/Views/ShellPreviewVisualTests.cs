// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using System.Runtime.InteropServices;
using Files.Core.Browsing;
using Files.Core.Capabilities.Previews;
using Files.Core.Storage;
using Files.Core.Windows;
using Files.Views;
using Files.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;
using WNDPROC = Windows.Win32.Extras.ManagedWNDPROC;

namespace Files.UITests.Views;

/// <summary>
/// Verifies that the native preview surface is painted into the WinUI composition tree.
/// </summary>
[TestClass]
public sealed class ShellPreviewVisualTests
{
	private const uint PreviewMarkerColor = 0x00CB5327;

	/// <summary>
	/// Verifies that preview content is visible without a manual window resize after handler creation.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[UITestMethod]
	public async Task ShellPreviewVisualIsPaintedBeforeManualResize()
	{
		var preview = new TestPreviewModel();
		using var viewModel = new PreviewPaneViewModel(preview, new InlineDispatcher());
		var factory = new PaintedChildPreviewFactory();
		var pane = new PreviewPane
		{
			Width = 640,
			Height = 420,
			SessionFactory = factory,
			ViewModel = viewModel,
		};
		var window = new Window { Content = pane };
		preview.Publish(CreateReadySnapshot());
		try
		{
			var loaded = WaitForLoadedAsync(pane);
			window.Activate();
			pane.AttachWindow(window);
			await loaded;

			await WaitForConditionAsync(() => !pane.PreviewHostWindowHandle.IsNull, TimeSpan.FromSeconds(5));
			await WaitForConditionAsync(() => factory.Session is not null, TimeSpan.FromSeconds(5));
			await WaitForConditionAsync(() => pane.HasShellPreviewSession, TimeSpan.FromSeconds(5));
			await WaitForConditionAsync(() => pane.HasShellPreviewComposition, TimeSpan.FromSeconds(5));
			await WaitForConditionAsync(() => pane.HasAppliedShellPreviewLayout, TimeSpan.FromSeconds(5));
			await WaitForConditionAsync(() => HasPaintedPixels(pane.PreviewHostWindowHandle), TimeSpan.FromSeconds(5));

			Assert.IsTrue(pane.IsShellPreviewVisualReady);
			Assert.IsNotNull(factory.Session);
			Assert.IsTrue(HasPaintedPixels(pane.PreviewHostWindowHandle), "The native preview surface remained visually blank before any resize.");

		}
		finally
		{
			await pane.DisposeAsync();
			window.Close();
		}
	}

	private static WindowsShellPreviewResult CreateReadyResult()
	{
		return new WindowsShellPreviewResult(new StorableReference(new StorageSourceId("ui-preview"), "painted-preview"), Guid.NewGuid());
	}

	private static BrowsePreviewSnapshot CreateReadySnapshot()
	{
		return new BrowsePreviewSnapshot(1, null, BrowsePreviewStatus.Ready, CreateReadyResult());
	}

	private static bool HasPaintedPixels(HWND windowHandle)
	{
		if (windowHandle.IsNull || !PInvoke.IsWindow(windowHandle) || !PInvoke.GetWindowRect(windowHandle, out var rectangle))
		{
			return false;
		}

		var deviceContext = PInvoke.GetDC(HWND.Null);
		if (deviceContext.IsNull)
		{
			return false;
		}

		try
		{
			return PInvoke.GetPixel(deviceContext, rectangle.left + 20, rectangle.top + 20).Value == PreviewMarkerColor;
		}
		finally
		{
			PInvoke.ReleaseDC(HWND.Null, deviceContext);
		}
	}

	private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
	{
		var stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < timeout)
		{
			if (condition())
			{
				return;
			}

			await Task.Delay(16);
		}

		Assert.Fail($"The preview did not reach the expected visual or input state within {timeout.TotalSeconds:F0} seconds.");
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

	private sealed class PaintedChildPreviewFactory : IWindowsShellPreviewSessionFactory
	{
		public PaintedChildPreviewSession? Session { get; private set; }

		public ValueTask<IWindowsShellPreviewSession> CreateAsync(WindowsShellPreviewResult result, WindowsPreviewHost host, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var session = new PaintedChildPreviewSession(host);
			Session = session;

			return ValueTask.FromResult<IWindowsShellPreviewSession>(session);
		}
	}

	private sealed class PaintedChildPreviewSession : IWindowsShellPreviewSession
	{
		private readonly string _className = $"ReFilesPreviewTest_{Guid.NewGuid():N}";
		private readonly WNDPROC _windowProc = PInvoke.DefWindowProc;
		private HBRUSH _backgroundBrush;
		private HWND _childWindow;
		private HWND _editWindow;

		public unsafe PaintedChildPreviewSession(WindowsPreviewHost host)
		{
			_backgroundBrush = PInvoke.CreateSolidBrush((COLORREF)PreviewMarkerColor);
			WNDCLASSEXW windowClass = default;
			windowClass.cbSize = (uint)sizeof(WNDCLASSEXW);
			windowClass.hInstance = PInvoke.GetModuleHandle(default(PCWSTR));
			windowClass.hbrBackground = _backgroundBrush;
			windowClass.lpfnWndProc = (delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, LRESULT>)Marshal.GetFunctionPointerForDelegate(_windowProc);
			fixed (char* className = _className)
			{
				windowClass.lpszClassName = className;
				Assert.AreNotEqual((ushort)0, PInvoke.RegisterClassEx(in windowClass));
				_childWindow = PInvoke.CreateWindowEx(0, className, null, WINDOW_STYLE.WS_CHILD | WINDOW_STYLE.WS_VISIBLE, 0, 0, host.Bounds.Width, host.Bounds.Height,
					host.WindowHandle, default, windowClass.hInstance, null);
			}

			Assert.IsFalse(_childWindow.IsNull);
			_editWindow = CreateEditWindow(_childWindow, host.Bounds);
		}

		public ValueTask SetBoundsAsync(WindowsPreviewBounds bounds, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (!_childWindow.IsNull && !PInvoke.SetWindowPos(_childWindow, HWND.Null, 0, 0, bounds.Width, bounds.Height, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW))
			{
				throw new InvalidOperationException($"The test preview window could not be positioned. Win32 error {Marshal.GetLastPInvokeError()}.");
			}

			_ = PInvoke.SetWindowPos(_editWindow, HWND.Null, 0, 40, bounds.Width, Math.Max(0, bounds.Height - 40), SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);

			return ValueTask.CompletedTask;
		}

		public ValueTask SetThemeAsync(WindowsPreviewColor background, WindowsPreviewColor foreground, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return ValueTask.CompletedTask;
		}

		public ValueTask SetFocusAsync(CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return ValueTask.CompletedTask;
		}

		public ValueTask<HWND> QueryFocusAsync(CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return ValueTask.FromResult(_childWindow);
		}

		public unsafe ValueTask DisposeAsync()
		{
			if (!_childWindow.IsNull && PInvoke.IsWindow(_childWindow))
			{
				PInvoke.DestroyWindow(_childWindow);
			}

			_childWindow = HWND.Null;
			_editWindow = HWND.Null;
			fixed (char* className = _className)
			{
				_ = PInvoke.UnregisterClass(className, PInvoke.GetModuleHandle(default(PCWSTR)));
			}

			if (!_backgroundBrush.IsNull)
			{
				_ = PInvoke.DeleteObject(_backgroundBrush);
				_backgroundBrush = HBRUSH.Null;
			}

			return ValueTask.CompletedTask;
		}

		private static unsafe HWND CreateEditWindow(HWND parent, WindowsPreviewBounds bounds)
		{
			const WINDOW_STYLE EditMultiline = (WINDOW_STYLE)0x0004;
			const WINDOW_STYLE EditReadOnly = (WINDOW_STYLE)0x0800;
			var style = WINDOW_STYLE.WS_CHILD | WINDOW_STYLE.WS_VISIBLE | EditMultiline | EditReadOnly;
			var childWindow = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_CLIENTEDGE, "EDIT", "REFILES_PREVIEW_PAINTED", style, 0, 40, bounds.Width, Math.Max(0, bounds.Height - 40), parent, null, null, null);
			if (childWindow.IsNull)
			{
				throw new InvalidOperationException($"The test preview window could not be created. Win32 error {Marshal.GetLastPInvokeError()}.");
			}

			return childWindow;
		}
	}

	private sealed class TestPreviewModel : IBrowsePreviewModel
	{
		public BrowsePreviewSnapshot Current { get; private set; } = new(0, null, BrowsePreviewStatus.Empty);

		public event EventHandler? Changed;

		public ValueTask RefreshAsync(PreviewHydrationPolicy hydrationPolicy = PreviewHydrationPolicy.LocalOnly, CancellationToken cancellationToken = default)
		{
			return ValueTask.CompletedTask;
		}

		public ValueTask PreviewUntrustedAsync(BrowsePreviewSnapshot blockedSnapshot, CancellationToken cancellationToken = default)
		{
			return ValueTask.CompletedTask;
		}

		public bool TryReportShellPreviewBlocked(BrowsePreviewSnapshot expectedSnapshot, PreviewBlockReason reason)
		{
			return false;
		}

		public ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}

		public void Publish(BrowsePreviewSnapshot snapshot)
		{
			Current = snapshot;
			Changed?.Invoke(this, EventArgs.Empty);
		}
	}

	private sealed class InlineDispatcher : Files.Infrastructure.IUIDispatcher
	{
		public bool HasThreadAccess => true;

		public bool TryEnqueue(Action callback)
		{
			callback();

			return true;
		}

		public bool TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority priority, Action callback)
		{
			callback();

			return true;
		}
	}
}

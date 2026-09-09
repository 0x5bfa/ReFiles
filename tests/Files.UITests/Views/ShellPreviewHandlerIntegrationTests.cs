// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using System.IO;
using System.Text;
using Files.Core.Browsing;
using Files.Core.Capabilities.Previews;
using Files.Core.Composition;
using Files.Core.Storage;
using Files.Core.Windows;
using Files.Views;
using Files.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Files.UITests.Views;

/// <summary>
/// Exercises installed Windows Shell preview handlers through the real ReFiles visual host.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ShellPreviewHandlerIntegrationTests
{
	private const string IntegrationEnvironmentVariable = "REFILES_RUN_SHELL_PREVIEW_INTEGRATION";
	private const string PdfPathEnvironmentVariable = "REFILES_SHELL_PREVIEW_PDF_PATH";

	/// <summary>
	/// Gets the MSTest context for integration diagnostics.
	/// </summary>
	public TestContext TestContext { get; set; } = null!;

	/// <summary>
	/// Verifies that the registered PDF preview handler paints before any manual resize.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[UITestMethod]
	[TestCategory("ExternalIntegration")]
	public async Task RegisteredPdfPreviewHandlerPaintsBeforeManualResize()
	{
		RequireIntegrationOptIn();
		var directoryPath = CreateTemporaryDirectory();
		var filePath = Path.Combine(directoryPath, "preview-paint-test.pdf");
		try
		{
			var sourcePath = Environment.GetEnvironmentVariable(PdfPathEnvironmentVariable);
			if (string.IsNullOrWhiteSpace(sourcePath))
			{
				File.WriteAllBytes(filePath, CreateTestPdf());
			}
			else
			{
				Assert.IsTrue(File.Exists(sourcePath), $"The PDF specified by {PdfPathEnvironmentVariable} does not exist: {sourcePath}");
				File.Copy(sourcePath, filePath);
				TestContext.WriteLine($"Using PDF integration fixture {sourcePath}.");
			}

			await VerifyRegisteredHandlerPaintsAsync(filePath, ".PDF");
		}
		finally
		{
			DeleteTemporaryFileAndDirectory(filePath, directoryPath);
		}
	}

	/// <summary>
	/// Verifies that the registered text preview handler, such as PowerToys Monaco, paints before any manual resize.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[UITestMethod]
	[TestCategory("ExternalIntegration")]
	public async Task RegisteredTextPreviewHandlerPaintsBeforeManualResize()
	{
		RequireIntegrationOptIn();
		var directoryPath = CreateTemporaryDirectory();
		var filePath = Path.Combine(directoryPath, "preview-paint-test.txt");
		try
		{
			File.WriteAllText(filePath, CreateTextPreviewContent(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			await VerifyRegisteredHandlerPaintsAsync(filePath, ".TXT");
		}
		finally
		{
			DeleteTemporaryFileAndDirectory(filePath, directoryPath);
		}
	}

	private static byte[] CreateTestPdf()
	{
		var content = new StringBuilder();
		for (var row = 0; row < 6; row++)
		{
			for (var column = 0; column < 4; column++)
			{
				var red = 0.12 + column * 0.21;
				var green = 0.12 + row * 0.12;
				var blue = 0.82 - column * 0.10 - row * 0.06;
				content.AppendLine(FormattableString.Invariant($"{red:F2} {green:F2} {blue:F2} rg"));
				content.AppendLine(FormattableString.Invariant($"{40 + column * 133} {70 + row * 112} 133 112 re f"));
			}
		}

		content.AppendLine("1 1 1 rg");
		content.AppendLine("BT /F1 32 Tf 70 735 Td (ReFiles PDF preview paint test) Tj ET");
		var contentText = content.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
		using var stream = new MemoryStream();
		var offsets = new long[6];
		WriteAscii(stream, "%PDF-1.4\n");
		WritePdfObject(stream, offsets, 1, "<< /Type /Catalog /Pages 2 0 R >>");
		WritePdfObject(stream, offsets, 2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
		WritePdfObject(stream, offsets, 3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>");
		offsets[4] = stream.Position;
		WriteAscii(stream, $"4 0 obj\n<< /Length {Encoding.ASCII.GetByteCount(contentText)} >>\nstream\n{contentText}endstream\nendobj\n");
		WritePdfObject(stream, offsets, 5, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
		var crossReferenceOffset = stream.Position;
		WriteAscii(stream, "xref\n0 6\n0000000000 65535 f \n");
		for (var index = 1; index < offsets.Length; index++)
		{
			WriteAscii(stream, $"{offsets[index]:D10} 00000 n \n");
		}

		WriteAscii(stream, $"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{crossReferenceOffset}\n%%EOF\n");

		return stream.ToArray();
	}

	private static string CreateTemporaryDirectory()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"ReFiles.ShellPreviewTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);

		return directoryPath;
	}

	private static string CreateTextPreviewContent()
	{
		var content = new StringBuilder();
		content.AppendLine("ReFiles Monaco preview paint test");
		for (var index = 0; index < 120; index++)
		{
			content.AppendLine($"line {index:D3}: public static string PreviewColor{index:D3} => \"painted-{index:D3}\"; // 0x{index * 65793:X6}");
		}

		return content.ToString();
	}

	private static void DeleteTemporaryFileAndDirectory(string filePath, string directoryPath)
	{
		if (File.Exists(filePath))
		{
			File.Delete(filePath);
		}

		if (Directory.Exists(directoryPath))
		{
			Directory.Delete(directoryPath, recursive: false);
		}
	}

	private static bool HasVisualVariation(HWND windowHandle, out int uniqueColorCount)
	{
		uniqueColorCount = 0;
		if (windowHandle.IsNull || !PInvoke.IsWindow(windowHandle) || !PInvoke.GetWindowRect(windowHandle, out var rectangle))
		{
			return false;
		}

		var width = rectangle.right - rectangle.left;
		var height = rectangle.bottom - rectangle.top;
		if (width < 32 || height < 32)
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
			var colors = new HashSet<uint>();
			for (var row = 1; row <= 20; row++)
			{
				for (var column = 1; column <= 24; column++)
				{
					var color = PInvoke.GetPixel(deviceContext, rectangle.left + width * column / 25, rectangle.top + height * row / 21).Value;
					if (color != uint.MaxValue)
					{
						colors.Add(color);
					}
				}
			}

			uniqueColorCount = colors.Count;

			return uniqueColorCount >= 6;
		}
		finally
		{
			PInvoke.ReleaseDC(HWND.Null, deviceContext);
		}
	}

	private static void RequireIntegrationOptIn()
	{
		if (!string.Equals(Environment.GetEnvironmentVariable(IntegrationEnvironmentVariable), "1", StringComparison.Ordinal))
		{
			Assert.Inconclusive($"Set {IntegrationEnvironmentVariable}=1 to run installed Shell preview handlers.");
		}
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

	private static void WriteAscii(Stream stream, string value)
	{
		stream.Write(Encoding.ASCII.GetBytes(value));
	}

	private static void WritePdfObject(Stream stream, long[] offsets, int objectNumber, string value)
	{
		offsets[objectNumber] = stream.Position;
		WriteAscii(stream, $"{objectNumber} 0 obj\n{value}\nendobj\n");
	}

	private async Task VerifyRegisteredHandlerPaintsAsync(string filePath, string extension)
	{
		var rawClsid = new WindowsShellPreviewHandlerAssociation().QueryPreviewHandler(extension);
		if (string.IsNullOrWhiteSpace(rawClsid) || !Guid.TryParse(rawClsid.Trim(), out var handlerClsid))
		{
			Assert.Inconclusive($"No valid Windows Shell preview handler is registered for {extension}.");

			return;
		}

		TestContext.WriteLine($"Testing {extension} preview handler {handlerClsid:B} with {filePath}.");
		await using var runtime = new FilesCoreBuilder().AddWindowsStorage(enableArchives: false).Build();
		Assert.IsNotNull(runtime.WindowsShellPreviewSessions);
		await using var model = await runtime.Workspace.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, filePath));
		var result = new WindowsShellPreviewResult(model.Reference, handlerClsid);
		var preview = new TestPreviewModel();
		using var viewModel = new PreviewPaneViewModel(preview, new InlineDispatcher());
		var pane = new PreviewPane
		{
			Width = 640,
			Height = 420,
			SessionFactory = runtime.WindowsShellPreviewSessions,
			ViewModel = viewModel,
		};
		var window = new Window { Content = pane };
		preview.Publish(new BrowsePreviewSnapshot(1, null, BrowsePreviewStatus.Ready, result));
		try
		{
			var loaded = WaitForLoadedAsync(pane);
			window.Activate();
			pane.AttachWindow(window);
			await loaded;

			var stopwatch = Stopwatch.StartNew();
			var uniqueColorCount = 0;
			while (stopwatch.Elapsed < TimeSpan.FromSeconds(30))
			{
				if (pane.IsShellPreviewVisualReady && HasVisualVariation(pane.PreviewHostWindowHandle, out uniqueColorCount))
				{
					TestContext.WriteLine($"The preview painted {uniqueColorCount} sampled colors after {stopwatch.Elapsed.TotalMilliseconds:F0} ms without resizing.");

					return;
				}

				await Task.Delay(50);
			}

			var originalSize = window.AppWindow.Size;
			window.AppWindow.Resize(new SizeInt32(originalSize.Width, originalSize.Height + 1));
			var paintedAfterResize = false;
			var resizeStopwatch = Stopwatch.StartNew();
			while (resizeStopwatch.Elapsed < TimeSpan.FromSeconds(10))
			{
				if (HasVisualVariation(pane.PreviewHostWindowHandle, out uniqueColorCount))
				{
					paintedAfterResize = true;

					break;
				}

				await Task.Delay(50);
			}

			Assert.Fail($"The {extension} handler did not paint a non-blank visual within 30 seconds before any resize. VisualReady={pane.IsShellPreviewVisualReady}, sampledColors={uniqueColorCount}, paintedAfterOnePixelResize={paintedAfterResize}.");
		}
		finally
		{
			await pane.DisposeAsync();
			window.Close();
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

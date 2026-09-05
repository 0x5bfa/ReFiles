// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Diagnostics;
using System.Runtime.Versioning;
using Files.Core.Diagnostics;
using Files.Core.Capabilities.Thumbnails;

namespace Files.Core.Windows;

[SupportedOSPlatform("windows6.0.6000")]
internal sealed class WindowsShellThumbnailSource : IThumbnailSource
{
	private readonly WindowsShellItemResolver _resolver;
	private readonly WindowsShellThumbnailBackend _backend;
	private readonly WindowsItemLocator _locator;

	public WindowsShellThumbnailSource(WindowsShellItemResolver resolver, WindowsShellThumbnailBackend backend, WindowsItemLocator locator)
	{
		ArgumentNullException.ThrowIfNull(resolver);
		ArgumentNullException.ThrowIfNull(backend);
		ArgumentNullException.ThrowIfNull(locator);

		_resolver = resolver;
		_backend = backend;
		_locator = locator;
	}

	public async ValueTask<ThumbnailResult?> GetThumbnailAsync(ThumbnailRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		var startTimestamp = Stopwatch.GetTimestamp();
		CoreDiagnosticLog.Write("WindowsShellThumbnailSource", $"GetThumbnail START size={request.RequestedPixelSize} mode={request.Mode} parsingName={_locator.ParsingName}");

		try
		{
			var payload = await _resolver.InvokeConcurrentAsync(_locator, shellItem => _backend.GetThumbnail(shellItem, _locator, request, cancellationToken), cancellationToken).ConfigureAwait(false);
			CoreDiagnosticLog.Write(
				"WindowsShellThumbnailSource",
				$"GetThumbnail END hasResult={payload is not null} bytes={payload?.Content.Length ?? 0} elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");

			return payload is null
				? null
				: new ThumbnailResult(payload.Content, payload.ContentType, payload.IsFallback, payload.Format, payload.PixelWidth, payload.PixelHeight);
		}
		catch (Exception exception)
		{
			CoreDiagnosticLog.Write(
				"WindowsShellThumbnailSource",
				$"GetThumbnail ERROR type={exception.GetType().Name} message={exception.Message} elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");

			throw;
		}
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Diagnostics;
using System.Runtime.Versioning;
using Files.Core.Capabilities.Thumbnails;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Files.Core.Storage.Windows;

[SupportedOSPlatform("windows6.0.6000")]
internal sealed class WindowsShellThumbnailSource : IThumbnailSource
{
	private readonly IWindowsShellScheduler _scheduler;
	private readonly WindowsShellThumbnailBackend _backend;
	private readonly WindowsItemLocator _locator;
	private readonly ILogger<WindowsShellThumbnailSource> _logger;

	public WindowsShellThumbnailSource(IWindowsShellScheduler scheduler, WindowsShellThumbnailBackend backend, WindowsItemLocator locator,
		ILogger<WindowsShellThumbnailSource>? logger = null)
	{
		ArgumentNullException.ThrowIfNull(scheduler);

		ArgumentNullException.ThrowIfNull(backend);

		ArgumentNullException.ThrowIfNull(locator);

		_scheduler = scheduler;
		_backend = backend;
		_locator = locator;
		_logger = logger ?? NullLogger<WindowsShellThumbnailSource>.Instance;
	}

	public async ValueTask<ThumbnailResult?> GetThumbnailAsync(ThumbnailRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		var startTimestamp = Stopwatch.GetTimestamp();
		if (_logger.IsEnabled(LogLevel.Debug))
		{
			_logger.LogDebug("{Component}: {Message}", "WindowsShellThumbnailSource", $"GetThumbnail START size={request.RequestedPixelSize} mode={request.Mode} parsingName={_locator.ParsingName}");
		}

		try
		{
			var payload = await _scheduler.InvokeConcurrentAsync(() =>
			{
				var shellItem = WindowsShellItemResolver.TryResolveOnCurrentSta(_locator);

				return shellItem is null ? null : _backend.GetThumbnail(shellItem, _locator, request, cancellationToken);
			}, cancellationToken).ConfigureAwait(false);
			if (_logger.IsEnabled(LogLevel.Debug))
			{
				_logger.LogDebug(
					"{Component}: {Message}",
					"WindowsShellThumbnailSource",
					$"GetThumbnail END hasResult={payload is not null} bytes={payload?.Content.Length ?? 0} elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}"
				);
			}

			return payload is null
				? null
				: new ThumbnailResult(payload.Content, payload.ContentType, payload.IsFallback, payload.Format, payload.PixelWidth, payload.PixelHeight);
		}
		catch (Exception exception)
		{
			if (_logger.IsEnabled(LogLevel.Debug))
			{
				_logger.LogDebug(
					"{Component}: {Message}",
					"WindowsShellThumbnailSource",
					$"GetThumbnail ERROR type={exception.GetType().Name} message={exception.Message} elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}"
				);
			}

			throw;
		}
	}
}

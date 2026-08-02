// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Files.Core.ItemFeatures.Thumbnails;

namespace Files.Core.Storage.Windows;

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

		var payload = await _resolver.InvokeConcurrentAsync(_locator, shellItem => _backend.GetThumbnail(shellItem, _locator, request, cancellationToken), cancellationToken).ConfigureAwait(false);

		return payload is null
			? null
			: new ThumbnailResult(payload.Content, payload.ContentType, payload.IsFallback);
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Files.Core.ItemFeatures.Thumbnails;

namespace Files.Core.Storage.Windows;

[SupportedOSPlatform("windows6.0.6000")]
internal sealed class WindowsShellThumbnailSource : IThumbnailSource
{
	private readonly WindowsShellItemResolver resolver;
	private readonly WindowsShellThumbnailBackend backend;
	private readonly WindowsItemLocator locator;

	public WindowsShellThumbnailSource(
		WindowsShellItemResolver resolver,
		WindowsShellThumbnailBackend backend,
		WindowsItemLocator locator)
	{
		ArgumentNullException.ThrowIfNull(resolver);
		ArgumentNullException.ThrowIfNull(backend);
		ArgumentNullException.ThrowIfNull(locator);

		this.resolver = resolver;
		this.backend = backend;
		this.locator = locator;
	}

	public async ValueTask<ThumbnailResult?> GetThumbnailAsync(
		ThumbnailRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		var payload = await resolver
			.InvokeConcurrentAsync(
				locator,
				shellItem => backend.GetThumbnail(
					shellItem,
					locator,
					request,
					cancellationToken),
				cancellationToken)
			.ConfigureAwait(false);

		return payload is null
			? null
			: new ThumbnailResult(payload.Content, payload.ContentType, payload.IsFallback);
	}
}

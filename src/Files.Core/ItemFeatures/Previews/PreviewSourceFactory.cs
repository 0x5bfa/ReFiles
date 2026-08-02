// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;

namespace Files.Core.ItemFeatures.Previews;

/// <summary>
/// Binds a shared preview loader to one item.
/// </summary>
public sealed class PreviewSourceFactory
	: IItemFeatureFactory<IPreviewSource>
{
	private readonly IPreviewLoader loader;

	public PreviewSourceFactory(IPreviewLoader loader)
	{
		ArgumentNullException.ThrowIfNull(loader);
		this.loader = loader;
	}

	public IPreviewSource? Create(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return loader.CanLoad(context)
			? new BoundPreviewSource(loader, context)
			: null;
	}

	private sealed class BoundPreviewSource : IPreviewSource
	{
		private readonly IPreviewLoader loader;
		private readonly ItemContext context;

		public BoundPreviewSource(IPreviewLoader loader, ItemContext context)
		{
			this.loader = loader;
			this.context = context;
		}

		public ValueTask<PreviewResult?> GetPreviewAsync(PreviewRequest request, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(request);

			return loader.GetPreviewAsync(request, context, cancellationToken);
		}
	}
}

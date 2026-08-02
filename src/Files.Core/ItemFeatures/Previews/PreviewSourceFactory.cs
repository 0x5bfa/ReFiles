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
	private readonly IPreviewLoader _loader;

	public PreviewSourceFactory(IPreviewLoader loader)
	{
		ArgumentNullException.ThrowIfNull(loader);

		_loader = loader;
	}

	public IPreviewSource? Create(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return _loader.CanLoad(context)
			? new BoundPreviewSource(_loader, context)
			: null;
	}

	private sealed class BoundPreviewSource : IPreviewSource
	{
		private readonly IPreviewLoader _loader;
		private readonly ItemContext _context;

		public BoundPreviewSource(IPreviewLoader loader, ItemContext context)
		{
			_loader = loader;
			_context = context;
		}

		public ValueTask<PreviewResult?> GetPreviewAsync(PreviewRequest request, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(request);

			return _loader.GetPreviewAsync(request, _context, cancellationToken);
		}
	}
}

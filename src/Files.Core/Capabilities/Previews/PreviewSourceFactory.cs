// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;

namespace Files.Core.Capabilities.Previews;

/// <summary>
/// Binds a shared preview loader to one item.
/// </summary>
public sealed class PreviewSourceFactory
	: ICapabilityFactory<IPreviewSource>
{
	private readonly IPreviewLoader _loader;

	/// <summary>Initializes a preview source factory.</summary>
	/// <param name="loader">The shared preview loader.</param>
	public PreviewSourceFactory(IPreviewLoader loader)
	{
		ArgumentNullException.ThrowIfNull(loader);

		_loader = loader;
	}

	/// <summary>Creates a preview source bound to an item context.</summary>
	/// <param name="context">The item context.</param>
	/// <returns>The bound source, or <see langword="null"/> when the loader does not apply.</returns>
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

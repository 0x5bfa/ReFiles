// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures.Previews;

namespace Files.Core.Composition;

/// <summary>
/// Adds storage-independent stream preview features.
/// </summary>
public static class PreviewFilesCoreBuilderExtensions
{
	private const string DefaultStreamPreviewsModule = "Files.Core.Previews.DefaultStreams";

	public static FilesCoreBuilder AddDefaultStreamPreviews(this FilesCoreBuilder builder, IPreviewStreamAccessPolicy? policy = null)
	{
		ArgumentNullException.ThrowIfNull(builder);

		if (!builder.TryAddModule(DefaultStreamPreviewsModule))
		{
			return builder;
		}

		var contentTypes = new ExtensionPreviewContentTypeResolver(
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				[".bmp"] = "image/bmp",
				[".csv"] = "text/csv",
				[".gif"] = "image/gif",
				[".htm"] = "text/html",
				[".html"] = "text/html",
				[".jpeg"] = "image/jpeg",
				[".jpg"] = "image/jpeg",
				[".json"] = "application/json",
				[".md"] = "text/markdown",
				[".png"] = "image/png",
				[".txt"] = "text/plain",
				[".webp"] = "image/webp",
				[".xml"] = "application/xml",
			});
		var loader = new StreamPreviewLoader(contentTypes, policy ?? AllowPreviewStreamAccessPolicy.Instance);
		builder.ItemFeatures.Add<IPreviewSource>(new PreviewSourceFactory(loader), priority: 200, origin: "Core stream preview");

		return builder;
	}
}

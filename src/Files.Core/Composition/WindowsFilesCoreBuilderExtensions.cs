// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Files.Core.Capabilities.Changes;
using Files.Core.Capabilities.Previews;
using Files.Core.Capabilities.Properties;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Storage.Archives;
using Files.Core.Storage.Windows;

namespace Files.Core.Composition;

/// <summary>
/// Adds the Windows Shell vertical slice to a Core runtime.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public static class WindowsFilesCoreBuilderExtensions
{
	private const string WindowsCapabilitiesModule = "Files.Core.Windows.Capabilities";
	private const string WindowsShellPreviewsModule = "Files.Core.Previews.WindowsShell";

	/// <summary>Registers Windows Shell storage and its optional preview, thumbnail, property, change, and archive capabilities.</summary>
	/// <param name="builder">The composition builder.</param>
	/// <param name="source">An optional existing Windows storage source.</param>
	/// <param name="streamPreviewPolicy">The optional stream preview policy.</param>
	/// <param name="shellPreviewPolicy">The optional Windows Shell preview policy.</param>
	/// <param name="enablePreviews">Whether to register preview loaders.</param>
	/// <param name="enableArchives">Whether to register archive browsing.</param>
	/// <param name="archiveCredentialResolver">The optional archive credential resolver.</param>
	/// <returns>The builder.</returns>
	public static FilesCoreBuilder AddWindowsStorage(
		this FilesCoreBuilder builder,
		WindowsStorageSource? source = null,
		IPreviewStreamAccessPolicy? streamPreviewPolicy = null,
		IWindowsShellPreviewPolicy? shellPreviewPolicy = null,
		bool enablePreviews = true,
		bool enableArchives = true,
		IArchiveCredentialResolver? archiveCredentialResolver = null)
	{
		ArgumentNullException.ThrowIfNull(builder);

		var windowsSource = source ?? new WindowsStorageSource();

		try
		{
			builder.AddStorageSource(windowsSource).AddStorageOperationHandler(new WindowsStorageOperationHandler(windowsSource));
		}
		catch (Exception registrationError) when (source is null)
		{
			try
			{
				windowsSource.DisposeAsync().AsTask().GetAwaiter().GetResult();
			}
			catch (Exception cleanupError)
			{
				throw new AggregateException("Windows storage registration and cleanup failed.", registrationError, cleanupError);
			}

			throw;
		}

		if (builder.TryAddModule(WindowsCapabilitiesModule))
		{
			builder.Capabilities
				.Add<IThumbnailSource>(new WindowsThumbnailSourceFactory(new WindowsShellThumbnailBackend()), priority: 100, origin: "Windows Shell")
				.Add<IPropertySource>(new PropertySourceFactory(new WindowsPropertyReader()), priority: 100, origin: "Windows Shell")
				.Add<IFolderChangeSource>(new FolderChangeSourceFactory(), priority: 100, origin: "Windows Shell");
		}

		if (enablePreviews)
		{
			var defaultPreviewPolicy = new WindowsPreviewAccessPolicy();
			builder.AddDefaultStreamPreviews(streamPreviewPolicy ?? defaultPreviewPolicy);
			AddWindowsShellPreviews(builder, shellPreviewPolicy ?? defaultPreviewPolicy);
		}

		if (enableArchives)
		{
			builder.AddArchiveBrowsing(archiveCredentialResolver);
		}

		return builder;
	}

	private static void AddWindowsShellPreviews(FilesCoreBuilder builder, IWindowsShellPreviewPolicy policy)
	{
		if (!builder.TryAddModule(WindowsShellPreviewsModule))
		{
			return;
		}

		var handlerResolver = new WindowsPreviewHandlerResolver();
		var loader = new WindowsShellPreviewLoader(handlerResolver, policy);
		builder.Capabilities.Add<IPreviewSource>(new PreviewSourceFactory(loader), priority: 100, origin: "Windows Shell preview handler");

		var previewScheduler = new WindowsShellScheduler(concurrentWorkerCount: 1);
		builder.Own(previewScheduler);
		builder.SetWindowsShellPreviewSessionFactory(workspace => new WindowsShellPreviewSessionFactory(workspace, previewScheduler, policy));
	}
}

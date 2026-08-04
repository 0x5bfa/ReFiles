// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Files.Core.ItemFeatures.Changes;
using Files.Core.ItemFeatures.Previews;
using Files.Core.ItemFeatures.Properties;
using Files.Core.ItemFeatures.Thumbnails;
using Files.Core.Storage.Archives;
using Files.Core.Storage.Windows;

namespace Files.Core.Composition;

/// <summary>
/// Adds the Windows Shell vertical slice to a Core runtime.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public static class WindowsFilesCoreBuilderExtensions
{
	private const string WindowsItemFeaturesModule = "Files.Core.Windows.ItemFeatures";
	private const string WindowsShellPreviewsModule = "Files.Core.Previews.WindowsShell";

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

		if (builder.TryAddModule(WindowsItemFeaturesModule))
		{
			builder.ItemFeatures
				.Add<IThumbnailSource>(new WindowsThumbnailSourceFactory(new WindowsShellThumbnailBackend()), priority: 100, origin: "Windows Shell")
				.Add<IPropertySource>(new PropertySourceFactory(new WindowsPropertyReader()), priority: 100, origin: "Windows Shell")
				.Add<IFolderChangeSource>(new FolderChangeSourceFactory(), priority: 100, origin: "Windows Shell");
		}

		if (enablePreviews)
		{
			builder.AddDefaultStreamPreviews(streamPreviewPolicy ?? AllowPreviewStreamAccessPolicy.Instance);
			AddWindowsShellPreviews(builder, shellPreviewPolicy ?? AllowWindowsShellPreviewPolicy.Instance);
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

		var handlerResolver = new WindowsPreviewHandlerResolver(new WindowsShellPreviewHandlerAssociation());
		var loader = new WindowsShellPreviewLoader(handlerResolver, policy);
		builder.ItemFeatures.Add<IPreviewSource>(new PreviewSourceFactory(loader), priority: 100, origin: "Windows Shell preview handler");

		var previewScheduler = new WindowsShellScheduler(concurrentWorkerCount: 1);
		builder.Own(previewScheduler);
		builder.SetWindowsShellPreviewSessionFactory(workspace => new WindowsShellPreviewSessionFactory(workspace, previewScheduler));
	}
}

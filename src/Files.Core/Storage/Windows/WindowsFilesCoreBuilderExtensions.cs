// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Files.Core.Capabilities;
using Files.Core.Browsing;
using Files.Core.Capabilities.Changes;
using Files.Core.Capabilities.Content;
using Files.Core.Capabilities.Previews;
using Files.Core.Capabilities.Properties;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Storage.Archives;
using Files.Core.Composition;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Adds the Windows Shell vertical slice to a Core runtime.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public static class WindowsFilesCoreBuilderExtensions
{
	private const string WindowsCapabilitiesModule = "Files.Core.Storage.Windows.Capabilities";
	private const string WindowsShellPreviewsModule = "Files.Core.Previews.WindowsShell";

	/// <summary>Registers Windows Shell storage and its optional preview, thumbnail, property, change, and archive capabilities.</summary>
	/// <param name="builder">The composition builder.</param>
	/// <param name="source">An optional existing Windows storage source.</param>
	/// <param name="shellPreviewPolicy">The optional Windows Shell preview policy.</param>
	/// <param name="enablePreviews">Whether to register preview loaders.</param>
	/// <param name="enableArchives">Whether to register archive browsing.</param>
	/// <param name="archiveCredentialResolver">The optional archive credential resolver.</param>
	/// <param name="enableOperations">Whether to register local operation execution.</param>
	/// <returns>The builder.</returns>
	public static FilesCoreBuilder AddWindowsStorage(
		this FilesCoreBuilder builder,
		WindowsStorageSource? source = null,
		IWindowsShellPreviewPolicy? shellPreviewPolicy = null,
		bool enablePreviews = true,
		bool enableArchives = true,
		IArchiveCredentialResolver? archiveCredentialResolver = null,
		bool enableOperations = true)
	{
		ArgumentNullException.ThrowIfNull(builder);

		if (enableOperations)
		{
			builder.EnsureLocalStorageOperations();
		}

		var windowsSource = source ?? new WindowsStorageSource();

		try
		{
			builder.AddStorageSource(windowsSource);
			if (enableOperations)
			{
				builder.AddStorageOperationHandler(new WindowsStorageOperationHandler(windowsSource));
			}
			builder.AddStorageBrowseLocationHandler(workspace => new WindowsSearchBrowseLocationHandler(workspace, windowsSource));
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
			var contentFactory = new WindowsContentSourceFactory();
			builder.Capabilities
				.Add<IReadableContent>(contentFactory, origin: "Windows content")
				.Add<IWritableContent>(contentFactory, origin: "Windows content")
				.Add<IFolderBrowseMetadata>(new DelegateCapabilityFactory<IFolderBrowseMetadata>(static context => context.CoreModel is WindowsFolder folder ? new WindowsFolderBrowseMetadata(folder) : null))
				.Add<IThumbnailSource>(new WindowsThumbnailSourceFactory(new WindowsShellThumbnailBackend()), priority: 100, origin: "Windows Shell")
				.Add<IPropertySource>(new PropertySourceFactory(new WindowsPropertyReader()), priority: 100, origin: "Windows Shell")
				.Add<IFolderChangeSource>(new FolderChangeSourceFactory(), priority: 100, origin: "Windows Shell");
		}

		if (enablePreviews)
		{
			AddWindowsShellPreviews(builder, shellPreviewPolicy ?? new WindowsPreviewAccessPolicy());
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

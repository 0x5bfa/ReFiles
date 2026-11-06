// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Files.Core.Browsing;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Changes;
using Files.Core.Capabilities.Content;
using Files.Core.Capabilities.Previews;
using Files.Core.Capabilities.Properties;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Data;
using Files.Core.Storage.Archives;
using Files.Core.Storage.Windows;
using Microsoft.Extensions.Logging;

namespace Files.Core;

/// <summary>Registers built-in storage providers through provider-facing runtime contracts.</summary>
public static partial class FilesProviderExtensions
{
	private const string WindowsCapabilitiesModule = "Files.Core.Storage.Windows.Capabilities";
	private const string WindowsShellPreviewsModule = "Files.Core.Previews.WindowsShell";

	/// <summary>Registers Windows Shell storage and its optional preview, thumbnail, property, change, and archive capabilities.</summary>
	/// <param name="source">An optional existing Windows storage source.</param>
	/// <param name="shellPreviewPolicy">The optional Windows Shell preview policy.</param>
	/// <param name="enablePreviews">Whether to register preview loaders.</param>
	/// <param name="enableArchives">Whether to register archive browsing.</param>
	/// <param name="archiveCredentialResolver">The optional archive credential resolver.</param>
	/// <param name="enableOperations">Whether to register local operation execution.</param>
	/// <typeparam name="TBuilder">The runtime registration implementation.</typeparam>
	/// <param name="builder">The runtime registration target.</param>
	/// <returns>This builder.</returns>
	[SupportedOSPlatform("windows6.0.6000")]
	public static TBuilder AddWindowsStorage<TBuilder>(this TBuilder builder, WindowsStorageSource? source = null, IWindowsShellPreviewPolicy? shellPreviewPolicy = null, bool enablePreviews = true,
		bool enableArchives = true,
		IArchiveCredentialResolver? archiveCredentialResolver = null, bool enableOperations = true) where TBuilder : class, IProviderRuntimeBuilder
	{
		ArgumentNullException.ThrowIfNull(builder);

		if (enableOperations)
		{
			builder.EnsureLocalStorageOperations();
		}

		var windowsSource = source ?? new WindowsStorageSource(loggerFactory: builder.LoggerFactory);

		try
		{
			builder.AddStorageSource(windowsSource);
			if (enableOperations)
			{
				builder.AddStorageOperationHandler(new WindowsStorageOperationHandler(windowsSource));
			}

			builder.AddBrowseLocationHandler(workspace => new WindowsSearchBrowseLocationHandler(workspace, windowsSource));
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
				.Add<IFolderBrowseMetadata>(new DelegateCapabilityFactory<IFolderBrowseMetadata>(static context =>
					context.Source is WindowsStorageSource source && context.CoreModel is WindowsFolder folder ? new WindowsFolderBrowseMetadata(folder, source.Scheduler) : null))
				.Add<IThumbnailSource>(new WindowsThumbnailSourceFactory(builder.LoggerFactory), priority: 100, origin: "Windows Shell")
				.Add<IFolderChangeSource>(new FolderChangeSourceFactory(), priority: 100, origin: "Windows Shell");
			builder.AddPropertyReader(new WindowsPropertyReader(builder.LoggerFactory.CreateLogger<WindowsPropertyReader>()), priority: 100, origin: "Windows Shell");
		}

		if (enablePreviews && builder.TryAddModule(WindowsShellPreviewsModule))
		{
			var policy = shellPreviewPolicy ?? new WindowsPreviewAccessPolicy();
			var handlerResolver = new WindowsPreviewHandlerResolver();
			var loader = new WindowsShellPreviewLoader(handlerResolver, policy);
			builder.AddPreviewLoader(loader, priority: 100, origin: "Windows Shell preview handler");

			var previewScheduler = new WindowsShellScheduler(concurrentWorkerCount: 1, logger: builder.LoggerFactory.CreateLogger<WindowsShellScheduler>());
			builder.Own(previewScheduler);
			builder.SetProviderServiceFactory<IWindowsShellPreviewSessionFactory>(workspace => new WindowsShellPreviewSessionFactory(workspace, previewScheduler, policy));
		}

		if (enableArchives)
		{
			builder.AddArchiveBrowsing(archiveCredentialResolver);
		}

		return builder;
	}

	/// <summary>Gets the borrowed Windows Shell preview integration registered by the Windows module.</summary>
	/// <param name="runtime">The configured runtime.</param>
	/// <returns>The registered integration, or null when unavailable.</returns>
	public static IWindowsShellPreviewSessionFactory? GetWindowsShellPreviewSessions(this IFilesCoreRuntime runtime)
	{
		ArgumentNullException.ThrowIfNull(runtime);

		return (runtime as IProviderRuntimeServices)?.GetProviderService<IWindowsShellPreviewSessionFactory>();
	}
}

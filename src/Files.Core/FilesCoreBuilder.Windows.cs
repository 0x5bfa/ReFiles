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

public sealed partial class FilesCoreBuilder
{
	private const string WindowsCapabilitiesModule = "Files.Core.Storage.Windows.Capabilities";
	private const string WindowsShellPreviewsModule = "Files.Core.Previews.WindowsShell";

	private Func<IStorageWorkspace, IWindowsShellPreviewSessionFactory?>? _windowsShellPreviewSessionFactory;

	/// <summary>Registers Windows Shell storage and its optional preview, thumbnail, property, change, and archive capabilities.</summary>
	/// <param name="source">An optional existing Windows storage source.</param>
	/// <param name="shellPreviewPolicy">The optional Windows Shell preview policy.</param>
	/// <param name="enablePreviews">Whether to register preview loaders.</param>
	/// <param name="enableArchives">Whether to register archive browsing.</param>
	/// <param name="archiveCredentialResolver">The optional archive credential resolver.</param>
	/// <param name="enableOperations">Whether to register local operation execution.</param>
	/// <returns>This builder.</returns>
	[SupportedOSPlatform("windows6.0.6000")]
	public FilesCoreBuilder AddWindowsStorage(WindowsStorageSource? source = null, IWindowsShellPreviewPolicy? shellPreviewPolicy = null, bool enablePreviews = true, bool enableArchives = true,
		IArchiveCredentialResolver? archiveCredentialResolver = null, bool enableOperations = true)
	{
		if (enableOperations)
		{
			EnsureLocalStorageOperations();
		}

		var windowsSource = source ?? new WindowsStorageSource(loggerFactory: LoggerFactory);

		try
		{
			AddStorageSource(windowsSource);
			if (enableOperations)
			{
				AddStorageOperationHandler(new WindowsStorageOperationHandler(windowsSource));
			}
			AddStorageBrowseLocationHandler(workspace => new WindowsSearchBrowseLocationHandler(workspace, windowsSource));
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

		if (TryAddModule(WindowsCapabilitiesModule))
		{
			var contentFactory = new WindowsContentSourceFactory();
			Capabilities
				.Add<IReadableContent>(contentFactory, origin: "Windows content")
				.Add<IWritableContent>(contentFactory, origin: "Windows content")
				.Add<IFolderBrowseMetadata>(new DelegateCapabilityFactory<IFolderBrowseMetadata>(static context => context.CoreModel is WindowsFolder folder ? new WindowsFolderBrowseMetadata(folder) : null))
				.Add<IThumbnailSource>(new WindowsThumbnailSourceFactory(new WindowsShellThumbnailBackend(), LoggerFactory), priority: 100, origin: "Windows Shell")
				.Add<IPropertySource>(new PropertySourceFactory(new WindowsPropertyReader(LoggerFactory.CreateLogger<WindowsPropertyReader>())), priority: 100, origin: "Windows Shell")
				.Add<IFolderChangeSource>(new FolderChangeSourceFactory(), priority: 100, origin: "Windows Shell");
		}

		if (enablePreviews)
		{
			AddWindowsShellPreviews(shellPreviewPolicy ?? new WindowsPreviewAccessPolicy());
		}

		if (enableArchives)
		{
			AddArchiveBrowsing(archiveCredentialResolver);
		}

		return this;
	}

	private void AddWindowsShellPreviews(IWindowsShellPreviewPolicy policy)
	{
		if (!TryAddModule(WindowsShellPreviewsModule))
		{
			return;
		}

		var handlerResolver = new WindowsPreviewHandlerResolver();
		var loader = new WindowsShellPreviewLoader(handlerResolver, policy);
		Capabilities.Add<IPreviewSource>(new PreviewSourceFactory(loader), priority: 100, origin: "Windows Shell preview handler");

		var previewScheduler = new WindowsShellScheduler(concurrentWorkerCount: 1, logger: LoggerFactory.CreateLogger<WindowsShellScheduler>());
		Own(previewScheduler);
		SetWindowsShellPreviewSessionFactory(workspace => new WindowsShellPreviewSessionFactory(workspace, previewScheduler, policy));
	}

	private void SetWindowsShellPreviewSessionFactory(Func<IStorageWorkspace, IWindowsShellPreviewSessionFactory> factory)
	{
		EnsureMutable();
		ArgumentNullException.ThrowIfNull(factory);

		if (_windowsShellPreviewSessionFactory is not null)
		{
			throw new InvalidOperationException("A Windows Shell preview session factory is already configured.");
		}

		_windowsShellPreviewSessionFactory = factory;
	}
}

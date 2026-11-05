// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Storage.Ftp;
using Files.Core.Storage.Windows;
using Files.Core.ViewSettings;
using Microsoft.Extensions.Logging;

namespace Files.Hosting;

/// <summary>Creates process runtimes without exposing provider construction or registration machinery.</summary>
public static class FilesHost
{
	/// <summary>Creates a UI-process runtime from borrowed application services.</summary>
	/// <param name="viewSettingsStore">The optional borrowed settings store.</param>
	/// <param name="thumbnailCache">The optional borrowed thumbnail cache.</param>
	/// <param name="operations">The optional external operation executor.</param>
	/// <param name="loggerFactory">The optional borrowed logger factory.</param>
	/// <returns>The owned runtime exposing consumer contracts.</returns>
	public static IUiFilesRuntime CreateUiRuntime(IViewSettingsStore? viewSettingsStore = null, IThumbnailCache? thumbnailCache = null,
		Files.Core.Storage.IStorageOperationService? operations = null, ILoggerFactory? loggerFactory = null)
	{
		return new UiRuntimeView(CreateRuntime(false, viewSettingsStore, thumbnailCache, operations, loggerFactory));
	}

	/// <summary>Creates a runtime for independently owned operation execution.</summary>
	/// <param name="loggerFactory">The optional borrowed logger factory.</param>
	/// <returns>The owned operation runtime.</returns>
	public static IOperationHostRuntime CreateOperationRuntime(ILoggerFactory? loggerFactory = null)
	{
		return new OperationRuntimeView(CreateRuntime(true, null, null, null, loggerFactory));
	}

	/// <summary>Creates a presenter-owned prefetch coordinator.</summary>
	/// <param name="session">The borrowed browse session.</param>
	/// <param name="loggerFactory">The optional borrowed logger factory.</param>
	/// <returns>The coordinator to dispose before the session.</returns>
	public static Files.Core.Browsing.IBrowsePrefetchCoordinator CreateBrowsePrefetch(Files.Core.Browsing.IBrowseSession session, ILoggerFactory? loggerFactory = null)
	{
		return new Files.Core.Browsing.BrowsePrefetchCoordinator(session, logger: loggerFactory?.CreateLogger<Files.Core.Browsing.BrowsePrefetchCoordinator>());
	}

	/// <summary>Creates an independent materialized thumbnail cache.</summary>
	/// <returns>The cache.</returns>
	public static IThumbnailCache CreateThumbnailCache()
	{
		return new MemoryThumbnailCache();
	}

	/// <summary>Creates independently owned settings pane content.</summary>
	/// <returns>The new settings content.</returns>
	public static Files.Core.Sessions.ISettingsPaneSession CreateSettingsContent()
	{
		return new Files.Core.Sessions.SettingsPaneSession();
	}

	/// <summary>Creates the stateless Windows property editor service.</summary>
	/// <returns>The property action service.</returns>
	public static IWindowsShellPropertyActions CreateWindowsPropertyActions()
	{
		return new WindowsShellPropertyActions();
	}

	/// <summary>Opens application settings persistence without constructing the storage runtime.</summary>
	/// <param name="localFolderPath">The application's local-state directory.</param>
	/// <returns>A settings-only service confined to that directory.</returns>
	public static Files.Core.Settings.ISettingsStorage OpenSettingsStorage(string localFolderPath)
	{
		return new WindowsSettingsStorage(localFolderPath);
	}

	/// <summary>Creates a filesystem property inspector with the application's operation executor.</summary>
	/// <param name="operations">The optional borrowed attribute operation executor.</param>
	/// <returns>The property inspector.</returns>
	public static IItemPropertiesFileSystem CreateItemPropertiesFileSystem(IFileAttributeOperationService? operations = null)
	{
		return new WindowsItemPropertiesFileSystem(operations);
	}

	private static FilesCoreRuntime CreateRuntime(bool operationHost, IViewSettingsStore? settings, IThumbnailCache? cache,
		Files.Core.Storage.IStorageOperationService? operations, ILoggerFactory? loggerFactory)
	{
		var builder = new FilesCoreBuilder(settings, cache, loggerFactory);
		try
		{
			if (operations is not null)
			{
				builder.UseStorageOperations(operations);
			}

			builder.AddWindowsStorage(enablePreviews: !operationHost, enableOperations: operations is null);
			if (operationHost)
			{
				builder.AddStorageTransfers();
			}

			return builder.Build();
		}
		catch (Exception creationError)
		{
			try
			{
				builder.DisposeAsync().AsTask().GetAwaiter().GetResult();
			}
			catch (Exception cleanupError)
			{
				throw new AggregateException("Runtime creation and cleanup failed.", creationError, cleanupError);
			}

			throw;
		}
	}

	private class RuntimeView(FilesCoreRuntime runtime) : IFilesCoreRuntime
	{
		protected FilesCoreRuntime Runtime { get; } = runtime;

		public Files.Core.Data.IStorageReader Workspace => ((IFilesCoreRuntime)Runtime).Workspace;
		public Files.Core.Sessions.IFilesApplicationSession ShellSession => Runtime.ShellSession;
		public Files.Core.Browsing.IBrowseLocationResolver LocationResolver => Runtime.LocationResolver;
		public Files.Core.Storage.IStorageOperationService StorageOperations => Runtime.StorageOperations;
		public IViewSettingsStore ViewSettingsStore => Runtime.ViewSettingsStore;
		public IThumbnailCache ThumbnailCache => Runtime.ThumbnailCache;

		public Files.Core.Sessions.IPaneSession CreateBrowsePane()
		{
			return Runtime.CreateBrowsePane();
		}

		public Files.Core.Sessions.IPaneSession CreateSettingsPane()
		{
			return Runtime.CreateSettingsPane();
		}

		public ValueTask DisposeAsync()
		{
			return Runtime.DisposeAsync();
		}
	}

	private sealed class UiRuntimeView : RuntimeView, IUiFilesRuntime
	{
		public IWindowsShellUiServices Windows { get; }

		public UiRuntimeView(FilesCoreRuntime runtime) : base(runtime)
		{
			Windows = new WindowsShellUiServices(runtime.Workspace.Sources.OfType<WindowsStorageSource>().Single(), runtime.GetWindowsShellPreviewSessions());
		}
	}

	private sealed class OperationRuntimeView : RuntimeView, IOperationHostRuntime
	{
		private readonly WindowsShellNewMenu _newMenu;

		public OperationRuntimeView(FilesCoreRuntime runtime) : base(runtime)
		{
			_newMenu = new WindowsShellNewMenu(runtime.Workspace.Sources.OfType<WindowsStorageSource>().Single().Scheduler);
		}

		public Files.Core.Storage.StorageSourceId RegisterFtpConnection(FtpConnectionProfile profile, IFtpCredentialResolver credentialResolver)
		{
			ArgumentNullException.ThrowIfNull(profile);
			ArgumentNullException.ThrowIfNull(credentialResolver);

			var source = new FtpStorageSource(profile, credentialResolver);
			try
			{
				Runtime.Workspace.SourceRegistry!.AddSource(source);

				return source.SourceId;
			}
			catch
			{
				source.DisposeAsync().AsTask().GetAwaiter().GetResult();
				throw;
			}
		}

		public Task<IReadOnlyList<WindowsShellNewItem>> GetNewItemsAsync(string folderPath, CancellationToken cancellationToken = default)
		{
			return _newMenu.GetItemsAsync(folderPath, cancellationToken);
		}

		public Task<bool> InvokeNewItemAsync(string folderPath, uint commandOffset, CancellationToken cancellationToken = default)
		{
			return _newMenu.InvokeAsync(folderPath, commandOffset, cancellationToken);
		}

		public void ApplyFileAttributes(IReadOnlyList<string> paths, bool? readOnly, bool? hidden, bool? archive, bool? indexed, bool? compressed, bool? encrypted, bool applyToContents,
			CancellationToken cancellationToken = default)
		{
			WindowsFileAttributeService.ApplyChanges(paths, readOnly, hidden, archive, indexed, compressed, encrypted, applyToContents, cancellationToken);
		}
	}
}

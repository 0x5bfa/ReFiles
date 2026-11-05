// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Data;
using Files.Core.Sessions;
using Files.Core.Storage;
using Files.Core.ViewSettings;

namespace Files.Core;

/// <summary>Owns one process runtime while exposing only consumer services.</summary>
public interface IFilesCoreRuntime : IAsyncDisposable
{
	/// <summary>Gets the borrowed source descriptions and item resolver.</summary>
	IStorageReader Workspace { get; }
	/// <summary>Gets the application session graph.</summary>
	IFilesApplicationSession ShellSession { get; }
	/// <summary>Gets the typed browse location resolver.</summary>
	IBrowseLocationResolver LocationResolver { get; }
	/// <summary>Gets the selected operation execution service.</summary>
	IStorageOperationService StorageOperations { get; }
	/// <summary>Gets the view settings store.</summary>
	IViewSettingsStore ViewSettingsStore { get; }
	/// <summary>Gets the thumbnail cache.</summary>
	IThumbnailCache ThumbnailCache { get; }
	/// <summary>Creates an independently owned browse pane.</summary>
	/// <returns>The new pane.</returns>
	IPaneSession CreateBrowsePane();
	/// <summary>Creates an independently owned settings pane.</summary>
	/// <returns>The new pane.</returns>
	IPaneSession CreateSettingsPane();
}

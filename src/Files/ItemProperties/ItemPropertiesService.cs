// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Data;
using Files.ViewModels;
using Files.StorageOperations;
using Files.Core.Storage.Windows;
using Microsoft.UI.Xaml;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT.Interop;

namespace Files.ItemProperties;

internal sealed partial class ItemPropertiesService : IItemPropertiesService, IDisposable
{
	private readonly nint _owner;
	private readonly IStorageWorkspace _workspace;
	private readonly IWindowsShellIconResources _iconResources;
	private readonly IItemPropertiesFileSystem _fileSystem;
	private readonly WindowsShellPropertiesService? _shellProperties;
	private readonly IStorageOperationService _storageOperations;
	private readonly HashSet<ItemPropertiesWindow> _windows = [];
	private bool _isDisposed;

	internal ItemPropertiesService(nint owner, IStorageWorkspace workspace, IStorageOperationService storageOperations, IWindowsShellIconResources iconResources,
		WindowsShellPropertiesService? shellProperties = null, IItemPropertiesFileSystem? fileSystem = null)
	{
		ArgumentNullException.ThrowIfNull(workspace);

		ArgumentNullException.ThrowIfNull(storageOperations);

		ArgumentNullException.ThrowIfNull(iconResources);

		_owner = owner;
		_workspace = workspace;
		_iconResources = iconResources;
		_fileSystem = fileSystem ?? new ItemPropertiesFileSystem(storageOperations as WinRTStorageOperationService);
		_shellProperties = shellProperties;
		_storageOperations = storageOperations;
	}

	public Task ShowAsync(IReadOnlyList<BrowseItemViewModel> items)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		ArgumentNullException.ThrowIfNull(items);

		var references = items.Select(static item => item.Reference).ToArray();
		Func<int, CancellationToken, Task<ThumbnailResult?>>? getIcon = null;
		if (references.Length is 1)
		{
			getIcon = async (size, cancellationToken) =>
			{
				await using var model = await _workspace.ResolveAsync(references[0], cancellationToken);
				var thumbnails = model.Get<IThumbnailSource>();

				return thumbnails is null ? null : await thumbnails.GetThumbnailAsync(new ThumbnailRequest(size, ThumbnailMode.Icon), cancellationToken);
			};
		}

		var window = new ItemPropertiesWindow(
			items,
			_storageOperations,
			_fileSystem,
			_iconResources,
			getIcon,
			_shellProperties is null ? null : cancellationToken => _shellProperties.GetPropertyPagesAsync(references, cancellationToken),
			_shellProperties is null ? null : (kind, cancellationToken) => _shellProperties.GetPropertyPageDataAsync(references, kind, cancellationToken),
			_shellProperties is null || references.Length is not 1 ? null : async cancellationToken =>
			{
				await using var model = await _workspace.ResolveAsync(references[0], cancellationToken);

				return await _shellProperties.GetGeneralPropertiesAsync(model, cancellationToken);
			});
		unsafe
		{
			PInvoke.SetWindowLongPtr(new(WindowNative.GetWindowHandle(window)), WINDOW_LONG_PTR_INDEX.GWLP_HWNDPARENT, _owner);
		}

		window.Closed += Window_Closed;
		_windows.Add(window);
		window.Activate();

		return Task.CompletedTask;
	}

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}

		_isDisposed = true;
		foreach (var window in _windows.ToArray())
		{
			window.Closed -= Window_Closed;
			window.Close();
		}

		_windows.Clear();
	}

	private void Window_Closed(object sender, WindowEventArgs args)
	{
		if (sender is ItemPropertiesWindow window)
		{
			window.Closed -= Window_Closed;
			_windows.Remove(window);
		}
	}
}

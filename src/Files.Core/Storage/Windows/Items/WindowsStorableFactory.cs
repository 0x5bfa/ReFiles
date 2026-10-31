// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.IO;
using Windows.Win32;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Resolves Shell identities and creates apartment-neutral storage models.
/// </summary>
internal sealed class WindowsStorableFactory
{
	private readonly IWindowsShellScheduler _scheduler;

	private readonly IWindowsItemIdReader _itemIdReader;

	private readonly WindowsFolderEnumerator _enumerator;

	internal WindowsStorableFactory(IWindowsShellScheduler scheduler, IWindowsItemIdReader itemIdReader, WindowsFolderEnumerator enumerator)
	{
		ArgumentNullException.ThrowIfNull(scheduler);
		ArgumentNullException.ThrowIfNull(itemIdReader);
		ArgumentNullException.ThrowIfNull(enumerator);

		_scheduler = scheduler;
		_itemIdReader = itemIdReader;
		_enumerator = enumerator;
	}

	public Task<WindowsStorable> CreateAsync(string parsingName, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);

		return _scheduler.InvokeAsync<WindowsStorable>(() =>
		{
			var descriptor = WindowShellHelpers.CreateDescriptor(WindowShellHelpers.ResolveItem(parsingName), _itemIdReader);

			return descriptor.Metadata.IsFolder ? new WindowsFolder(descriptor, this, _enumerator) : new WindowsFile(descriptor, this);
		}, cancellationToken);
	}

	public Task<WindowsStorable> CreateAsync(Guid knownFolderId, CancellationToken cancellationToken = default)
	{
		return _scheduler.InvokeAsync<WindowsStorable>(() =>
		{
			var hr = PInvoke.SHGetKnownFolderItem(knownFolderId, KNOWN_FOLDER_FLAG.KF_FLAG_DEFAULT, null, out IShellItem shellItem);
			hr.ThrowOnFailure();
			var descriptor = WindowShellHelpers.CreateDescriptor(shellItem, _itemIdReader);

			return descriptor.Metadata.IsFolder ? new WindowsFolder(descriptor, this, _enumerator) : new WindowsFile(descriptor, this);
		}, cancellationToken);
	}

	internal Task<WindowsStorable> CreateDesktopAsync(CancellationToken cancellationToken = default)
	{
		return _scheduler.InvokeAsync<WindowsStorable>(() =>
		{
			ITEMIDLIST desktopPidl = default;
			var hr = PInvoke.SHCreateItemFromIDList(in desktopPidl, out IShellItem shellItem);
			hr.ThrowOnFailure();
			var descriptor = WindowShellHelpers.CreateDescriptor(shellItem, _itemIdReader);

			return descriptor.Metadata.IsFolder ? new WindowsFolder(descriptor, this, _enumerator) : new WindowsFile(descriptor, this);
		}, cancellationToken);
	}

	internal Task<WindowsFolder> CreateSearchFolderAsync(string query, IReadOnlyList<WindowsItemLocator>? scopeLocators, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(query);

		if (scopeLocators is { Count: 0 })
		{
			throw new ArgumentException("A supplied Windows Shell search scope cannot be empty.", nameof(scopeLocators));
		}

		return _scheduler.InvokeAsync(() =>
		{
			var shellItem = WindowsShellSearchFolderFactory.Create(query, scopeLocators);
			var descriptor = WindowShellHelpers.CreateDescriptor(shellItem, _itemIdReader) with { IsSearchFolder = true };
			if (!descriptor.Metadata.IsFolder)
			{
				throw new InvalidOperationException("The Windows Shell search factory did not return a folder.");
			}

			return new WindowsFolder(descriptor, this, _enumerator);
		}, cancellationToken);
	}

	public Task<WindowsStorable?> TryCreateAsync(string parsingName, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(parsingName))
		{
			return Task.FromResult<WindowsStorable?>(null);
		}

		return _scheduler.InvokeConcurrentAsync<WindowsStorable?>(() =>
		{
			var shellItem = WindowShellHelpers.TryResolveItem(parsingName);
			if (shellItem is null)
			{
				return null;
			}

			var descriptor = WindowShellHelpers.CreateDescriptor(shellItem, _itemIdReader);

			return descriptor.Metadata.IsFolder ? new WindowsFolder(descriptor, this, _enumerator) : new WindowsFile(descriptor, this);
		}, cancellationToken);
	}

	internal Task<WindowsStorable?> TryCreateFromAbsolutePidlAsync(ReadOnlyMemory<byte> absolutePidl, CancellationToken cancellationToken = default)
	{
		if (absolutePidl.IsEmpty)
		{
			return Task.FromResult<WindowsStorable?>(null);
		}

		return _scheduler.InvokeAsync<WindowsStorable?>(() =>
		{
			var shellItem = WindowShellHelpers.TryResolveItem(absolutePidl);
			if (shellItem is null)
			{
				return null;
			}

			var descriptor = WindowShellHelpers.CreateDescriptor(shellItem, _itemIdReader);

			return descriptor.Metadata.IsFolder ? new WindowsFolder(descriptor, this, _enumerator) : new WindowsFile(descriptor, this);
		}, cancellationToken);
	}

	public async Task<WindowsStorable?> TryCreateFromItemIdAsync(string itemId, StorageAddress? lastKnownAddress = null, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

		if (_itemIdReader.TryGetParsingName(itemId, out var parsingName))
		{
			var addressCandidate = await TryCreateAsync(parsingName, cancellationToken).ConfigureAwait(false);

			return IsMatchingItem(addressCandidate, itemId) ? addressCandidate : null;
		}

		if (!_itemIdReader.IsFileSystemIdentity(itemId))
		{
			return null;
		}

		if (lastKnownAddress is null || !lastKnownAddress.Scheme.Equals(WindowsStorageSource.FileAddressScheme, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		var lastKnownPath = lastKnownAddress.Value;
		var directCandidate = await TryCreateAsync(lastKnownPath, cancellationToken).ConfigureAwait(false);
		if (IsMatchingItem(directCandidate, itemId))
		{
			return directCandidate;
		}

		var parentPath = Path.GetDirectoryName(lastKnownPath);
		if (string.IsNullOrWhiteSpace(parentPath) || !Directory.Exists(parentPath))
		{
			return null;
		}

		try
		{
			foreach (var candidatePath in Directory.EnumerateFileSystemEntries(parentPath))
			{
				cancellationToken.ThrowIfCancellationRequested();

				var candidate = await TryCreateAsync(candidatePath, cancellationToken).ConfigureAwait(false);
				if (IsMatchingItem(candidate, itemId))
				{
					return candidate;
				}
			}
		}
		catch (IOException)
		{
			return null;
		}
		catch (UnauthorizedAccessException)
		{
			return null;
		}

		return null;
	}

	public Task<WindowsFolder?> GetParentAsync(WindowsStorableDescriptor descriptor, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(descriptor);

		return _scheduler.InvokeAsync(() =>
		{
			var shellItem = WindowShellHelpers.TryResolveItem(descriptor.Locator);
			if (shellItem is null)
			{
				return null;
			}

			var hr = shellItem.GetParent(out var parent);

			if (hr.Failed)
			{
				return null;
			}

			var parentDescriptor = WindowShellHelpers.CreateDescriptor(parent, _itemIdReader);

			return parentDescriptor.Metadata.IsFolder ? new WindowsFolder(parentDescriptor, this, _enumerator) : null;
		}, cancellationToken);
	}

	private static bool IsMatchingItem(WindowsStorable? storable, string itemId)
	{
		return storable is not null && StringComparer.Ordinal.Equals(storable.Id, itemId);
	}
}

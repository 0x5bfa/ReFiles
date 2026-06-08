// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using OwlCore.Storage;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Resolves Shell interfaces on the ordered STA lane and returns managed models or affine wrappers.
/// </summary>
internal sealed class WindowsStorableFactory
{
	private readonly IWindowsShellScheduler scheduler;
	private readonly IWindowsItemIdReader itemIdReader;
	private readonly WindowsShellItemResolver resolver;

	public WindowsStorableFactory(
		IWindowsShellScheduler scheduler,
		IWindowsItemIdReader? itemIdReader = null)
	{
		ArgumentNullException.ThrowIfNull(scheduler);
		this.scheduler = scheduler;
		this.itemIdReader = itemIdReader ?? new WindowsItemIdReader();
		resolver = new WindowsShellItemResolver(scheduler);
	}

	internal WindowsShellItemResolver Resolver => resolver;

	public Task<WindowsStorable> CreateAsync(
		string parsingName,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);

		return resolver.InvokeAsync<WindowsStorable>(
			parsingName,
			shellItem => Create(ShellItemHelpers.CreateDescriptor(shellItem, itemIdReader)),
			cancellationToken);
	}

	public Task<WindowsStorable> CreateAsync(
		Guid knownFolderId,
		CancellationToken cancellationToken = default)
	{
		return scheduler.InvokeAsync<WindowsStorable>(
			() =>
			{
				var result = PInvoke.SHGetKnownFolderItem(
					knownFolderId,
					KNOWN_FOLDER_FLAG.KF_FLAG_DEFAULT,
					null,
					out IShellItem shellItem);
				result.ThrowOnFailure();
				return Create(ShellItemHelpers.CreateDescriptor(shellItem, itemIdReader));
			},
			cancellationToken);
	}

	public Task<WindowsStorable?> TryCreateAsync(
		string parsingName,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(parsingName))
		{
			return Task.FromResult<WindowsStorable?>(null);
		}

		return resolver.InvokeConcurrentAsync<WindowsStorable?>(
			parsingName,
			shellItem => Create(ShellItemHelpers.CreateDescriptor(shellItem, itemIdReader)),
			cancellationToken);
	}

	internal Task<WindowsStorable?> TryCreateFromAbsolutePidlAsync(
		ReadOnlyMemory<byte> absolutePidl,
		CancellationToken cancellationToken = default)
	{
		if (absolutePidl.IsEmpty)
		{
			return Task.FromResult<WindowsStorable?>(null);
		}

		return resolver.InvokeAsync<WindowsStorable?>(
			absolutePidl,
			shellItem => Create(ShellItemHelpers.CreateDescriptor(shellItem, itemIdReader)),
			cancellationToken);
	}

	public Task<WindowsStorable?> TryCreateFromItemIdAsync(
		string itemId,
		StorageAddress? lastKnownAddress = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

		return TryCreateFromItemIdCoreAsync(itemId, lastKnownAddress, cancellationToken);
	}

	private async Task<WindowsStorable?> TryCreateFromItemIdCoreAsync(
		string itemId,
		StorageAddress? lastKnownAddress,
		CancellationToken cancellationToken)
	{
		if (itemIdReader.TryGetParsingName(itemId, out var parsingName))
		{
			var addressCandidate = await TryCreateAsync(parsingName, cancellationToken)
				.ConfigureAwait(false);
			return IsMatchingItem(addressCandidate, itemId) ? addressCandidate : null;
		}

		if (!itemIdReader.IsFileSystemIdentity(itemId))
		{
			return null;
		}

		if (lastKnownAddress is null
			|| !lastKnownAddress.Scheme.Equals(
				WindowsStorageSource.FileAddressScheme,
				StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		var lastKnownPath = lastKnownAddress.Value;
		var directCandidate = await TryCreateAsync(lastKnownPath, cancellationToken)
			.ConfigureAwait(false);
		if (IsMatchingItem(directCandidate, itemId))
		{
			return directCandidate;
		}

		var parentPath = Path.GetDirectoryName(lastKnownPath);
		if (string.IsNullOrWhiteSpace(parentPath)
			|| !Directory.Exists(parentPath))
		{
			return null;
		}

		try
		{
			foreach (var candidatePath in Directory.EnumerateFileSystemEntries(parentPath))
			{
				cancellationToken.ThrowIfCancellationRequested();

				var candidate = await TryCreateAsync(candidatePath, cancellationToken)
					.ConfigureAwait(false);
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

	public Task<WindowsFolder?> GetParentAsync(
		WindowsStorableDescriptor descriptor,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(descriptor);

		return resolver.InvokeAsync<WindowsFolder?>(
			descriptor.Locator,
			shellItem =>
			{
				var parentResult = shellItem.GetParent(out var parent);

				if (parentResult.Failed)
				{
					return null;
				}

				return Create(ShellItemHelpers.CreateDescriptor(parent, itemIdReader)) as WindowsFolder;
			},
			cancellationToken);
	}

	public Task<ShellFolderEnumerator> CreateEnumeratorAsync(
		WindowsStorableDescriptor descriptor,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(descriptor);

		return resolver.InvokeAsync<ShellFolderEnumerator>(
			descriptor.Locator,
			shellItem =>
			{
				var bindResult = shellItem.BindToHandler(
					null,
					PInvoke.BHID_EnumItems,
					out IEnumShellItems? enumerator);
				bindResult.ThrowOnFailure();

				if (enumerator is null)
				{
					throw new InvalidOperationException("The Shell folder returned no item enumerator.");
				}

				return new ShellFolderEnumerator(
					scheduler,
					enumerator,
					itemIdReader);
			},
			cancellationToken);
	}

	public Task<Stream> OpenReadStreamAsync(
		WindowsStorableDescriptor descriptor,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(descriptor);

		return resolver.InvokeAsync<Stream>(
			descriptor.Locator,
			shellItem =>
			{
				var bindResult = shellItem.BindToHandler(
					null,
					PInvoke.BHID_Stream,
					out IStream? shellStream);
				bindResult.ThrowOnFailure();

				if (shellStream is null)
				{
					throw new IOException("The virtual Shell item returned no stream.");
				}

				return new ShellReadStream(scheduler, shellStream);
			},
			cancellationToken);
	}

	internal WindowsStorable Create(WindowsStorableDescriptor descriptor)
	{
		ArgumentNullException.ThrowIfNull(descriptor);

		return descriptor.Snapshot.IsFolder
			? new WindowsFolder(descriptor, this)
			: new WindowsFile(descriptor, this);
	}

	private static bool IsMatchingItem(
		WindowsStorable? storable,
		string itemId)
	{
		return storable is not null
			&& StringComparer.Ordinal.Equals(storable.Id, itemId);
	}
}

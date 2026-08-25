// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.CompilerServices;
using OwlCore.Storage;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Resolves file-system and virtual items through the Windows Shell namespace.
/// </summary>
public sealed class WindowsStorageSource : IStorageSource
{
	/// <summary>Gets the default Windows Shell source type.</summary>
	public const string DefaultSourceType = "windows-shell";

	/// <summary>Gets the file-system address scheme.</summary>
	public const string FileAddressScheme = "file";

	/// <summary>Gets the Shell address scheme.</summary>
	public const string ShellAddressScheme = "shell";

	private readonly IReadOnlyList<Guid> _rootFolderIds;

	private readonly WindowsStorableFactory _storableFactory;

	private readonly WindowsShellChangeWatcher _changeWatcher;

	private readonly bool _ownsScheduler;

	private readonly Lock _disposalLock = new();

	private Task? _disposeTask;

	private volatile bool _isDisposed;

	/// <summary>Gets the stable source identifier.</summary>
	public StorageSourceId SourceId { get; }

	/// <summary>Gets the source type.</summary>
	public string SourceType => DefaultSourceType;

	/// <summary>Gets the source display name.</summary>
	public string DisplayName { get; }

	/// <summary>
	/// Gets the shared scheduler used by Windows-backed item feature factories.
	/// </summary>
	public IWindowsShellScheduler Scheduler { get; }

	internal WindowsShellItemResolver ShellItemResolver => _storableFactory.Resolver;

	internal WindowsShellChangeWatcher ChangeWatcher => _changeWatcher;

	internal bool IsFileSystemIdentity(string itemId) => _storableFactory.IsFileSystemIdentity(itemId);

	/// <summary>Initializes a Windows Shell storage source.</summary>
	/// <param name="sourceId">The optional source identifier.</param>
	/// <param name="displayName">The source display name.</param>
	/// <param name="rootFolderIds">The known folder identifiers to expose.</param>
	/// <param name="scheduler">The optional Shell scheduler.</param>
	public WindowsStorageSource(StorageSourceId? sourceId = null, string displayName = "Windows", IEnumerable<Guid>? rootFolderIds = null, IWindowsShellScheduler? scheduler = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

		SourceId = sourceId ?? new StorageSourceId(DefaultSourceType);
		DisplayName = displayName;
		_rootFolderIds = Array.AsReadOnly((rootFolderIds ?? [FOLDERID.FOLDERID_ComputerFolder]).ToArray());
		Scheduler = scheduler ?? new WindowsShellScheduler();
		_ownsScheduler = scheduler is null;
		_storableFactory = new WindowsStorableFactory(Scheduler);
		_changeWatcher = new WindowsShellChangeWatcher(Scheduler);
	}

	internal Task<WindowsStorable?> TryCreateFromAbsolutePidlAsync(ReadOnlyMemory<byte> absolutePidl, CancellationToken cancellationToken = default)
	{
		return _storableFactory.TryCreateFromAbsolutePidlAsync(absolutePidl, cancellationToken);
	}

	/// <summary>Enumerates the configured Windows Shell roots.</summary>
	/// <param name="cancellationToken">The token used to cancel enumeration.</param>
	/// <returns>An asynchronous sequence of root folders.</returns>
	public async IAsyncEnumerable<IFolder> GetRootsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		foreach (var rootFolderId in _rootFolderIds)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var root = await _storableFactory.CreateAsync(rootFolderId, cancellationToken).ConfigureAwait(false);

			if (root is WindowsFolder folder)
			{
				yield return folder;
				continue;
			}

			throw new InvalidOperationException($"Known folder '{rootFolderId}' did not resolve to a folder.");
		}
	}

	/// <summary>Determines whether this source can resolve an address.</summary>
	/// <param name="address">The address to inspect.</param>
	/// <returns><see langword="true"/> when the scheme is supported.</returns>
	public bool CanResolve(StorageAddress address)
	{
		ArgumentNullException.ThrowIfNull(address);

		return address.Scheme.Equals(ShellAddressScheme, StringComparison.OrdinalIgnoreCase)
			|| address.Scheme.Equals(FileAddressScheme, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Resolves a Windows Shell address.</summary>
	/// <param name="address">The address to resolve.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The resolved item.</returns>
	public async ValueTask<IStorable> ResolveAsync(StorageAddress address, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		ArgumentNullException.ThrowIfNull(address);

		if (!CanResolve(address))
		{
			throw new ArgumentException($"Address scheme '{address.Scheme}' is not supported.", nameof(address));
		}

		return await _storableFactory.CreateAsync(address.Value, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Resolves a Windows Shell item reference.</summary>
	/// <param name="reference">The reference to resolve.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The resolved item.</returns>
	public async ValueTask<IStorable> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		ArgumentNullException.ThrowIfNull(reference);

		if (reference.SourceId != SourceId)
		{
			throw new ArgumentException($"Reference belongs to storage source '{reference.SourceId}'.", nameof(reference));
		}

		var storable = await _storableFactory.TryCreateFromItemIdAsync(reference.ItemId, reference.LastKnownAddress, cancellationToken).ConfigureAwait(false);

		if (storable is not null)
		{
			return storable;
		}

		var lastKnownAddress = reference.LastKnownAddress;
		if (lastKnownAddress is not null && CanResolve(lastKnownAddress))
		{
			var candidate = await _storableFactory.TryCreateAsync(lastKnownAddress.Value, cancellationToken).ConfigureAwait(false);

			if (candidate is not null && StringComparer.Ordinal.Equals(candidate.Id, reference.ItemId))
			{
				return candidate;
			}
		}

		throw new FileNotFoundException("The Windows Shell item could not be resolved.", reference.ItemId);
	}

	/// <summary>Determines whether a reference identifies the virtual Shell Desktop root.</summary>
	/// <param name="reference">The reference to inspect.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns><see langword="true"/> when the reference identifies the virtual Shell Desktop root.</returns>
	public async ValueTask<bool> IsShellDesktopAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		ArgumentNullException.ThrowIfNull(reference);

		if (reference.SourceId != SourceId)
		{
			return false;
		}

		var desktop = await _storableFactory.CreateDesktopAsync(cancellationToken).ConfigureAwait(false);

		return StringComparer.Ordinal.Equals(desktop.Id, reference.ItemId);
	}

	/// <summary>Disposes the source and any scheduler it owns.</summary>
	/// <returns>A value task that represents source disposal.</returns>
	public ValueTask DisposeAsync()
	{
		lock (_disposalLock)
		{
			if (_disposeTask is not null)
			{
				return new ValueTask(_disposeTask);
			}

			_isDisposed = true;
			_disposeTask = DisposeCoreAsync();

			return new ValueTask(_disposeTask);
		}
	}

	private async Task DisposeCoreAsync()
	{
		var errors = new List<Exception>();

		try
		{
			await _changeWatcher.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception error)
		{
			errors.Add(error);
		}

		if (_ownsScheduler)
		{
			try
			{
				await Scheduler.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception error)
			{
				errors.Add(error);
			}
		}

		GC.SuppressFinalize(this);

		if (errors.Count is 1)
		{
			throw errors[0];
		}

		if (errors.Count > 1)
		{
			throw new AggregateException("One or more Windows storage source resources could not be disposed.", errors);
		}
	}
}

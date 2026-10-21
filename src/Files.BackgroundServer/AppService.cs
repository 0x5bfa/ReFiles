// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Storage;
using Windows.Foundation;

namespace Files.BackgroundServer;

/// <summary>Connects a UI process to the shared operation host.</summary>
public sealed class AppService : IDisposable
{
	private readonly CancellationTokenSource _disconnected = new();
	private readonly CancellationToken _sessionToken;
	private Process? _process;
	private IDisposable? _clientLease;
	private Guid _serverClientId;
	private int _disposed;

	/// <summary>Attaches a client without transferring ownership of accepted operations.</summary>
	/// <param name="processId">The client process identity.</param>
	public AppService(int processId)
	{
		_sessionToken = _disconnected.Token;
		try
		{
			_clientLease = Program.Lifetime.Acquire(isClient: true);
			_serverClientId = Guid.NewGuid();
			BackgroundServerHost.AddClient(_serverClientId, processId);
			_process = Process.GetProcessById(processId);
			_process.Exited += Process_Exited;
			_process.EnableRaisingEvents = true;
		}
		catch
		{
			Dispose();

			throw;
		}
	}

	/// <summary>Captures current operation state for this server instance.</summary>
	/// <returns>A bounded snapshot whose items are returned by value.</returns>
	public OperationList GetOperations()
	{
		using var request = AcquireRequest();

		return Program.Operations.GetOperations();
	}

	/// <summary>Waits for a revision change or a bounded refresh timeout.</summary>
	/// <param name="serverInstanceId">The server identity from the last snapshot.</param>
	/// <param name="knownRevision">The revision from the last snapshot.</param>
	/// <returns>A cancelable observation request; canceling it never cancels an operation.</returns>
	public IAsyncAction WaitForOperationChangesAsync(Guid serverInstanceId, ulong knownRevision)
	{
		return AsyncInfo.Run(async cancellationToken =>
		{
			using var request = AcquireRequest();
			using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sessionToken);
			await Program.Operations.WaitForChangesAsync(serverInstanceId, knownRevision, cancellation.Token).ConfigureAwait(false);
		});
	}

	/// <summary>Requests cooperative cancellation without claiming completion.</summary>
	/// <param name="operationId">The accepted operation identity.</param>
	/// <returns>Whether the operation is still active and accepts cancellation.</returns>
	public bool CancelOperation(Guid operationId)
	{
		using var request = AcquireRequest();

		return Program.Operations.RequestCancellation(operationId);
	}

	/// <summary>Accepts an entire storage batch before starting its item loop.</summary>
	/// <param name="operationId">A new caller-generated operation identity.</param>
	/// <param name="items">The complete batch intent.</param>
	/// <returns>The accepted operation identity.</returns>
	public Guid SubmitStorageOperations(Guid operationId, [ReadOnlyArray] StorageOperationData[] items)
	{
		using var request = AcquireRequest();

		return Program.Operations.Submit(new StorageOperationBatch(operationId, items));
	}

	/// <summary>Accepts attribute changes for execution independently of the UI.</summary>
	public Guid SubmitAttributeChanges(Guid operationId, [ReadOnlyArray] string[] paths, FileAttributeChangesData changes)
	{
		using var request = AcquireRequest();

		return Program.Operations.Submit(new FileAttributeOperation(operationId, paths, changes));
	}

	/// <summary>Accepts attribute changes and a following rename as one server-owned operation.</summary>
	public Guid SubmitPropertyChanges(Guid operationId, [ReadOnlyArray] string[] paths, FileAttributeChangesData changes, StorageReferenceData item, string newName)
	{
		using var request = AcquireRequest();
		var rename = new StorageOperationData { Kind = StorageOperationKind.Rename, Item = item, Name = newName, DisplayName = newName };

		return Program.Operations.Submit(new FileAttributeOperation(operationId, paths, changes, rename));
	}

	/// <summary>Invokes a Shell New menu item in the server's own Shell apartment.</summary>
	public Guid SubmitShellNewItem(Guid operationId, string folderPath, uint commandOffset, string name)
	{
		using var request = AcquireRequest();
		ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		return Program.Operations.Submit(new ShellNewItemOperation(operationId, folderPath, commandOffset, name));
	}

	/// <summary>Gets the Windows Shell New menu items from the server's shared cache.</summary>
	/// <param name="folderPath">The absolute file-system path of the target folder.</param>
	/// <returns>A snapshot of the cached menu items.</returns>
	public IAsyncOperation<IReadOnlyList<ShellNewItemData>> GetShellNewItemsAsync(string folderPath)
	{
		return AsyncInfo.Run(async cancellationToken =>
		{
			using var request = AcquireRequest();
			ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

			return await Program.ShellNewItems.GetItemsAsync(folderPath, cancellationToken).ConfigureAwait(false);
		});
	}

	/// <summary>Registers one UI window with the server-owned tray host.</summary>
	/// <param name="windowHandle">The native window handle.</param>
	public void RegisterWindow(long windowHandle)
	{
		using var request = AcquireRequest();
		BackgroundServerHost.RegisterWindow(_serverClientId, windowHandle);
	}

	/// <summary>Marks one registered UI window as the most recently active window.</summary>
	/// <param name="windowHandle">The native window handle.</param>
	public void SetActiveWindow(long windowHandle)
	{
		using var request = AcquireRequest();
		BackgroundServerHost.SetActiveWindow(_serverClientId, windowHandle);
	}

	/// <summary>Unregisters one UI window from the server-owned tray host.</summary>
	/// <param name="windowHandle">The native window handle.</param>
	public void UnregisterWindow(long windowHandle)
	{
		using var request = AcquireRequest();
		BackgroundServerHost.UnregisterWindow(_serverClientId, windowHandle);
	}

	/// <summary>Gets one materialized thumbnail from the shared server cache.</summary>
	/// <param name="key">The thumbnail identity and size.</param>
	/// <returns>The cached entry, or a value with <see cref="ThumbnailCacheEntryData.HasValue"/> set to <see langword="false"/>.</returns>
	public ThumbnailCacheEntryData GetThumbnailCacheEntry(ThumbnailCacheKeyData key)
	{
		using var request = AcquireRequest();
		var cacheKey = ToThumbnailCacheKey(key);
		var entry = Program.Thumbnails.Value.GetAsync(cacheKey).GetAwaiter().GetResult();
		if (entry is null)
		{
			return default;
		}

		return new ThumbnailCacheEntryData
		{
			HasValue = true,
			ContentBase64 = Convert.ToBase64String(entry.Content.Span),
			ContentType = entry.ContentType,
			IsFallback = entry.IsFallback,
			Format = (int)entry.Format,
			PixelWidth = entry.PixelWidth,
			PixelHeight = entry.PixelHeight,
		};
	}

	/// <summary>Stores a materialized thumbnail in the shared server cache.</summary>
	/// <param name="key">The thumbnail identity and size.</param>
	/// <param name="entry">The thumbnail payload.</param>
	public void SetThumbnailCacheEntry(ThumbnailCacheKeyData key, ThumbnailCacheEntryData entry)
	{
		using var request = AcquireRequest();
		Program.Thumbnails.Value.SetAsync(ToThumbnailCacheKey(key), ToThumbnailCacheEntry(entry)).GetAwaiter().GetResult();
	}

	/// <summary>Gets the current cache invalidation version for a storage item.</summary>
	/// <param name="reference">The storage item identity.</param>
	/// <returns>The current invalidation version.</returns>
	public long GetThumbnailInvalidationVersion(StorageReferenceData reference)
	{
		using var request = AcquireRequest();

		return Program.Thumbnails.Value.GetInvalidationVersionAsync(ToStorableReference(reference)).GetAwaiter().GetResult();
	}

	/// <summary>Stores a thumbnail only when its invalidation version is still current.</summary>
	/// <param name="key">The thumbnail identity and size.</param>
	/// <param name="entry">The thumbnail payload.</param>
	/// <param name="expectedInvalidationVersion">The version observed before rendering the thumbnail.</param>
	/// <returns>Whether the entry was stored.</returns>
	public bool TrySetThumbnailCacheEntry(ThumbnailCacheKeyData key, ThumbnailCacheEntryData entry, long expectedInvalidationVersion)
	{
		using var request = AcquireRequest();

		return Program.Thumbnails.Value.TrySetAsync(ToThumbnailCacheKey(key), ToThumbnailCacheEntry(entry), expectedInvalidationVersion).GetAwaiter().GetResult();
	}

	/// <summary>Invalidates cached thumbnails for a storage item.</summary>
	/// <param name="reference">The storage item identity.</param>
	public void InvalidateThumbnails(StorageReferenceData reference)
	{
		using var request = AcquireRequest();
		Program.Thumbnails.Value.InvalidateAsync(ToStorableReference(reference)).GetAwaiter().GetResult();
	}

	/// <summary>Requests a pause or resume when the operation supports it.</summary>
	public bool SetOperationPaused(Guid operationId, bool paused)
	{
		using var request = AcquireRequest();

		return Program.Operations.SetPaused(operationId, paused);
	}

	/// <summary>Answers a specific interruption, rejecting obsolete decisions.</summary>
	public bool ResolveOperationInterruption(Guid operationId, Guid interruptionId, int decision, bool applyToAll)
	{
		using var request = AcquireRequest();

		return Program.Operations.ResolveInterruption(operationId, interruptionId, decision, applyToAll);
	}

	/// <summary>Removes a terminal operation from this server's retained history.</summary>
	/// <param name="operationId">The operation identity.</param>
	/// <returns>Whether a terminal operation was removed.</returns>
	public bool ForgetOperation(Guid operationId)
	{
		using var request = AcquireRequest();

		return Program.Operations.Forget(operationId);
	}

	/// <summary>Disconnects this client and its observation requests, leaving operations running.</summary>
	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		if (Interlocked.Exchange(ref _process, null) is { } process)
		{
			process.Exited -= Process_Exited;
			process.Dispose();
		}

		_disconnected.Cancel();
		_disconnected.Dispose();
		Interlocked.Exchange(ref _clientLease, null)?.Dispose();
		BackgroundServerHost.RemoveClient(_serverClientId);
		Program.Operations.ScheduleUnattendedCancellation();
		GC.SuppressFinalize(this);
	}

	~AppService() => Dispose();

	private IDisposable AcquireRequest()
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

		return Program.Lifetime.Acquire();
	}

	private void Process_Exited(object? sender, EventArgs e)
	{
		Dispose();
	}

	private static ThumbnailCacheKey ToThumbnailCacheKey(ThumbnailCacheKeyData data)
	{
		return new ThumbnailCacheKey(new StorageSourceId(data.SourceId), data.ItemId, data.RequestedSize, (ThumbnailMode)data.Mode);
	}

	private static ThumbnailCacheEntry ToThumbnailCacheEntry(ThumbnailCacheEntryData data)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(data.ContentBase64);

		return new ThumbnailCacheEntry(Convert.FromBase64String(data.ContentBase64), data.ContentType, data.IsFallback,
			(ThumbnailContentFormat)data.Format, data.PixelWidth, data.PixelHeight);
	}

	private static StorableReference ToStorableReference(StorageReferenceData data)
	{
		return new StorableReference(new StorageSourceId(data.SourceId), data.ItemId);
	}
}

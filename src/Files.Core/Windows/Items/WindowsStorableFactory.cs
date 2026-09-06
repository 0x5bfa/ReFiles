// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Files.Core.Diagnostics;
using Files.Core.Storage;
using Files.Core.ViewSettings;
using OwlCore.Storage;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Windows;

/// <summary>
/// Resolves Shell interfaces on the ordered STA lane and returns managed models or affine wrappers.
/// </summary>
internal sealed class WindowsStorableFactory
{
	private const int CanceledHResultValue = unchecked((int)0x800704C7);

	private const int EnumerationBatchSize = 32;

	private const int EnumerationBufferSize = 4;

	private const int IdentityWorkerCount = 4;

	private const int SearchEnumerationBatchSize = 1;

	private readonly IWindowsShellScheduler _scheduler;

	private readonly IWindowsItemIdReader _itemIdReader;

	private readonly WindowsShellItemResolver _resolver;

	internal WindowsShellItemResolver Resolver => _resolver;

	public WindowsStorableFactory(IWindowsShellScheduler scheduler, IWindowsItemIdReader? itemIdReader = null)
	{
		ArgumentNullException.ThrowIfNull(scheduler);

		_scheduler = scheduler;
		_itemIdReader = itemIdReader ?? new WindowsItemIdReader();
		_resolver = new WindowsShellItemResolver(scheduler);
	}

	public Task<WindowsStorable> CreateAsync(string parsingName, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);

		return _resolver.InvokeAsync(parsingName, shellItem => Create(ShellItemHelpers.CreateDescriptor(shellItem, _itemIdReader)), cancellationToken);
	}

	public Task<WindowsStorable> CreateAsync(Guid knownFolderId, CancellationToken cancellationToken = default)
	{
		return _scheduler.InvokeAsync(
			() =>
			{
				var hr = PInvoke.SHGetKnownFolderItem(knownFolderId, KNOWN_FOLDER_FLAG.KF_FLAG_DEFAULT, null, out IShellItem shellItem);
				hr.ThrowOnFailure();

				return Create(ShellItemHelpers.CreateDescriptor(shellItem, _itemIdReader));
			},
			cancellationToken);
	}

	internal Task<WindowsStorable> CreateDesktopAsync(CancellationToken cancellationToken = default)
	{
		return _scheduler.InvokeAsync(
			() =>
			{
				ITEMIDLIST desktopPidl = default;
				var hr = PInvoke.SHCreateItemFromIDList(in desktopPidl, out IShellItem shellItem);
				hr.ThrowOnFailure();

				return Create(ShellItemHelpers.CreateDescriptor(shellItem, _itemIdReader));
			},
			cancellationToken);
	}

	internal Task<WindowsFolder> CreateSearchFolderAsync(string query, IReadOnlyList<WindowsItemLocator>? scopeLocators, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(query);

		if (scopeLocators is { Count: 0 })
		{
			throw new ArgumentException("A supplied Windows Shell search scope cannot be empty.", nameof(scopeLocators));
		}

		return _scheduler.InvokeAsync(
			() =>
			{
				var shellItem = WindowsShellSearchFolderFactory.Create(query, scopeLocators);
				var descriptor = ShellItemHelpers.CreateDescriptor(shellItem, _itemIdReader) with { IsSearchFolder = true };
				var storable = Create(descriptor);

				return storable as WindowsFolder ?? throw new InvalidOperationException("The Windows Shell search factory did not return a folder.");
			},
			cancellationToken);
	}

	public Task<WindowsStorable?> TryCreateAsync(string parsingName, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(parsingName))
		{
			return Task.FromResult<WindowsStorable?>(null);
		}

		return _resolver.InvokeConcurrentAsync<WindowsStorable?>(parsingName, shellItem => Create(ShellItemHelpers.CreateDescriptor(shellItem, _itemIdReader)), cancellationToken);
	}

	internal Task<WindowsStorable?> TryCreateFromAbsolutePidlAsync(ReadOnlyMemory<byte> absolutePidl, CancellationToken cancellationToken = default)
	{
		if (absolutePidl.IsEmpty)
		{
			return Task.FromResult<WindowsStorable?>(null);
		}

		return _resolver.InvokeAsync<WindowsStorable?>(absolutePidl, shellItem => Create(ShellItemHelpers.CreateDescriptor(shellItem, _itemIdReader)), cancellationToken);
	}

	internal bool IsFileSystemIdentity(string itemId)
	{
		return _itemIdReader.IsFileSystemIdentity(itemId);
	}

	public Task<WindowsStorable?> TryCreateFromItemIdAsync(string itemId, StorageAddress? lastKnownAddress = null, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

		return TryCreateFromItemIdCoreAsync(itemId, lastKnownAddress, cancellationToken);
	}

	public Task<WindowsFolder?> GetParentAsync(WindowsStorableDescriptor descriptor, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(descriptor);

		return _resolver.InvokeAsync(
			descriptor.Locator,
			shellItem =>
			{
				var hr = shellItem.GetParent(out var parent);

				if (hr.Failed)
				{
					return null;
				}

				return Create(ShellItemHelpers.CreateDescriptor(parent, _itemIdReader)) as WindowsFolder;
			},
			cancellationToken);
	}

	internal Task<WindowsShellColumnSet> GetColumnsAsync(WindowsStorableDescriptor descriptor, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(descriptor);

		return _resolver.InvokeAsync(
			descriptor.Locator,
			shellItem => WindowsShellColumnReader.Read(shellItem, descriptor.Locator.ParsingName, cancellationToken),
			cancellationToken);
	}

	internal Task<BrowseViewSettingsOverride?> GetViewSettingsAsync(WindowsStorableDescriptor descriptor, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(descriptor);

		return _resolver.InvokeAsync(descriptor.Locator, shellItem => WindowsShellViewSettingsPersistence.Read(shellItem, descriptor.Locator.ParsingName, cancellationToken), cancellationToken);
	}

	internal Task<ViewSettingsPersistenceResult> SetViewSettingsAsync(WindowsStorableDescriptor descriptor, BrowseViewSettingsOverride settingsOverride, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(descriptor);

		ArgumentNullException.ThrowIfNull(settingsOverride);

		return _resolver.InvokeAsync(descriptor.Locator,
			shellItem => WindowsShellViewSettingsPersistence.Write(shellItem, descriptor.Locator.ParsingName, settingsOverride, cancellationToken), cancellationToken);
	}

	internal Task<BrowseViewSettingsOverride> ClearViewSettingsAsync(WindowsStorableDescriptor descriptor, ViewSettingsOverrideFields fields, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(descriptor);

		return _resolver.InvokeAsync(descriptor.Locator, shellItem => WindowsShellViewSettingsPersistence.Clear(shellItem, descriptor.Locator.ParsingName, fields, cancellationToken), cancellationToken);
	}

	internal async IAsyncEnumerable<WindowsStorableDescriptor> EnumerateChildrenAsync(WindowsStorableDescriptor descriptor, HWND ownerWindow,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(descriptor);

		var parentFolder = new WindowsItemLocator(descriptor.Locator.AbsolutePidl, descriptor.Locator.ParsingName);
		var enumerationStartTimestamp = Stopwatch.GetTimestamp();
		var batchCount = 0;
		var itemCount = 0;
		var identityDuration = TimeSpan.Zero;
		CoreDiagnosticLog.Write("WindowsStorableFactory", $"Enumerate START name={descriptor.Snapshot.Name} parsingName={descriptor.Locator.ParsingName}");

		var batches = Channel.CreateBounded<IReadOnlyList<WindowsStorableDescriptorData>>(new BoundedChannelOptions(EnumerationBufferSize)
		{
			SingleReader = true,
			SingleWriter = true,
			FullMode = BoundedChannelFullMode.Wait,
		});
		using var enumerationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		Task? producer = null;
		try
		{
			Func<IShellItem, bool> enumerateChildren = shellItem =>
				EnumerateChildrenOnCurrentSta(shellItem, parentFolder, descriptor.IsSearchFolder, ownerWindow, batches.Writer, enumerationCancellation.Token);
			var scheduledProducer = descriptor.IsSearchFolder
				? _resolver.InvokeSearchAsync(descriptor.Locator, enumerateChildren, enumerationCancellation.Token)
				: _resolver.InvokeConcurrentAsync(descriptor.Locator, enumerateChildren, enumerationCancellation.Token);
			producer = CompleteChannelWhenFinishedAsync(scheduledProducer, batches.Writer);

			await foreach (var batch in batches.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
			{
				batchCount++;
				itemCount += batch.Count;
				var identityStartTimestamp = Stopwatch.GetTimestamp();
				var childDescriptors = CreateDescriptors(batch, cancellationToken);
				identityDuration += Stopwatch.GetElapsedTime(identityStartTimestamp);

				foreach (var childDescriptor in childDescriptors)
				{
					cancellationToken.ThrowIfCancellationRequested();

					yield return childDescriptor;
				}
			}

			await producer.ConfigureAwait(false);
			cancellationToken.ThrowIfCancellationRequested();

		}
		finally
		{
			enumerationCancellation.Cancel();

			if (producer is not null)
			{
				try
				{
					await producer.ConfigureAwait(false);
				}
				catch (OperationCanceledException)
					when (enumerationCancellation.IsCancellationRequested)
				{
				}
			}

			CoreDiagnosticLog.Write(
				"WindowsStorableFactory",
				$"Enumerate END name={descriptor.Snapshot.Name} batches={batchCount} items={itemCount} " +
				$"identityMs={identityDuration.TotalMilliseconds:F1} elapsedMs={Stopwatch.GetElapsedTime(enumerationStartTimestamp).TotalMilliseconds:F1}");
		}
	}

	public Task<Stream> OpenStreamAsync(WindowsStorableDescriptor descriptor, FileAccess accessMode, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(descriptor);

		return _resolver.InvokeAsync<Stream>(
			descriptor.Locator,
			shellItem =>
			{
				var hr = PInvoke.CreateBindCtx(0, out IBindCtx? bindContext);
				hr.ThrowOnFailure();

				if (bindContext is null)
				{
					throw new IOException("Could not create a Shell bind context.");
				}

				BIND_OPTS bindOptions = default;
				bindOptions.cbStruct = (uint)Unsafe.SizeOf<BIND_OPTS>();
				bindOptions.grfMode = accessMode switch
				{
					FileAccess.Read => (uint)(STGM.STGM_READ | STGM.STGM_SHARE_DENY_NONE),
					FileAccess.Write => (uint)(STGM.STGM_WRITE | STGM.STGM_SHARE_DENY_WRITE),
					FileAccess.ReadWrite => (uint)(STGM.STGM_READWRITE | STGM.STGM_SHARE_DENY_WRITE),
					_ => throw new ArgumentOutOfRangeException(nameof(accessMode)),
				};
				hr = bindContext.SetBindOptions(in bindOptions);
				hr.ThrowOnFailure();

				hr = shellItem.BindToHandler(bindContext, PInvoke.BHID_Stream, out IStream? shellStream);
				hr.ThrowOnFailure();

				if (shellStream is null)
				{
					throw new IOException("The Shell item returned no stream.");
				}

				return new ShellReadStream(_scheduler, shellStream, accessMode);
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

	private WindowsStorableDescriptor[] CreateDescriptors(IReadOnlyList<WindowsStorableDescriptorData> items, CancellationToken cancellationToken)
	{
		var descriptors = new WindowsStorableDescriptor[items.Count];
		var parallelOptions = new ParallelOptions
		{
			CancellationToken = cancellationToken,
			MaxDegreeOfParallelism = IdentityWorkerCount,
			TaskScheduler = TaskScheduler.Default,
		};
		Parallel.For(0, items.Count, parallelOptions, index => descriptors[index] = ShellItemHelpers.CreateDescriptor(items[index], _itemIdReader));

		return descriptors;
	}

	private static ReadOnlyMemory<byte> CombinePidls(ReadOnlyMemory<byte> parentPidl, ReadOnlyMemory<byte> relativePidl)
	{
		if (parentPidl.Length < sizeof(ushort) || relativePidl.Length < sizeof(ushort))
		{
			throw new InvalidOperationException("The Shell returned an invalid item identifier.");
		}

		var absolutePidl = GC.AllocateUninitializedArray<byte>(checked(parentPidl.Length + relativePidl.Length - sizeof(ushort)));
		parentPidl.Span[..^sizeof(ushort)].CopyTo(absolutePidl);
		relativePidl.Span.CopyTo(absolutePidl.AsSpan(parentPidl.Length - sizeof(ushort)));

		return absolutePidl;
	}

	private async Task<WindowsStorable?> TryCreateFromItemIdCoreAsync(string itemId, StorageAddress? lastKnownAddress, CancellationToken cancellationToken)
	{
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

	private static unsafe bool EnumerateChildrenOnCurrentSta(
		IShellItem shellItem,
		WindowsItemLocator parentFolder,
		bool isSearchFolder,
		HWND ownerWindow,
		ChannelWriter<IReadOnlyList<WindowsStorableDescriptorData>> writer,
		CancellationToken cancellationToken)
	{
		var enumerationStartTimestamp = Stopwatch.GetTimestamp();
		var batchCount = 0;
		var itemCount = 0;
		var nextCallCount = 0;
		var nextDuration = TimeSpan.Zero;
		var descriptorDuration = TimeSpan.Zero;
		var channelWriteDuration = TimeSpan.Zero;
		CoreDiagnosticLog.Write("WindowsStorableFactory", "EnumerateOnSTA START");
		using var cancellationRegistration = cancellationToken.UnsafeRegister(static state => ((ChannelWriter<IReadOnlyList<WindowsStorableDescriptorData>>)state!).TryComplete(), writer);
		try
		{
			var folder = WindowsShellColumnReader.TryGetFolder(shellItem, parentFolder.ParsingName, cancellationToken);
			if (folder is null)
			{
				throw new InvalidOperationException("The Shell item returned no folder interface.");
			}

			var enumerationFlags = _SHCONTF.SHCONTF_FOLDERS | _SHCONTF.SHCONTF_NONFOLDERS | _SHCONTF.SHCONTF_INCLUDEHIDDEN;
			var hr = folder.EnumObjects(ownerWindow, (uint)enumerationFlags, out IEnumIDList? enumerator);
			if (hr == HRESULT.S_FALSE)
			{
				writer.TryComplete();

				return true;
			}

			ThrowIfEnumerationFailed(hr);
			if (enumerator is null)
			{
				throw new InvalidOperationException("The Shell folder returned no item enumerator.");
			}

			using var cancellationSite = isSearchFolder ? WindowsShellEnumerationCancellationSite.TryAttach(enumerator, cancellationToken) : null;
			var enumerationBatchSize = isSearchFolder ? SearchEnumerationBatchSize : EnumerationBatchSize;
			var batch = new List<WindowsStorableDescriptorData>(enumerationBatchSize);
			var childPidls = stackalloc ITEMIDLIST*[EnumerationBatchSize];
			var itemStore = WindowsShellItemStore.TryCreate(parentFolder.AbsolutePidl);

			while (true)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					CoreDiagnosticLog.Write(
						"WindowsStorableFactory",
						$"EnumerateOnSTA CANCELLED batches={batchCount} items={itemCount} elapsedMs={Stopwatch.GetElapsedTime(enumerationStartTimestamp).TotalMilliseconds:F1}");

					return false;
				}

				var nextStartTimestamp = Stopwatch.GetTimestamp();
				hr = enumerator.Next((uint)enumerationBatchSize, childPidls, out var fetched);
				nextCallCount++;
				nextDuration += Stopwatch.GetElapsedTime(nextStartTimestamp);

				if (hr == HRESULT.S_FALSE && fetched is 0)
				{
					break;
				}

				ThrowIfEnumerationFailed(hr);
				if (fetched is 0)
				{
					break;
				}

				var fetchedCount = checked((int)fetched);
				try
				{
					for (var index = 0; index < fetchedCount; index++)
					{
						cancellationToken.ThrowIfCancellationRequested();

						var childPidl = childPidls[index];
						if (childPidl is null)
						{
							continue;
						}

						var relativePidl = ShellItemHelpers.CopyPidl(childPidl);
						var absolutePidl = CombinePidls(parentFolder.AbsolutePidl, relativePidl);
						var itemStoreReference = itemStore?.TryInsert(folder, in *childPidl);
						var child = itemStoreReference?.TryGetItem(folder);
						if (child is null)
						{
							fixed (byte* absolutePidlBytes = absolutePidl.Span)
							{
								hr = PInvoke.SHCreateItemFromIDList(in *(ITEMIDLIST*)absolutePidlBytes, out child);
								hr.ThrowOnFailure();
							}
						}

						var descriptorStartTimestamp = Stopwatch.GetTimestamp();
						batch.Add(ShellItemHelpers.CreateDescriptorData(child, parentFolder, absolutePidl, relativePidl, itemStoreReference));
						descriptorDuration += Stopwatch.GetElapsedTime(descriptorStartTimestamp);

						itemCount++;
						if (batch.Count >= enumerationBatchSize)
						{
							batchCount++;
							var channelWriteStartTimestamp = Stopwatch.GetTimestamp();
							if (!WriteBatch(writer, batch, cancellationToken))
							{
								return false;
							}

							channelWriteDuration += Stopwatch.GetElapsedTime(channelWriteStartTimestamp);
							batch = new List<WindowsStorableDescriptorData>(enumerationBatchSize);
						}

						PInvoke.CoTaskMemFree(childPidl);
						childPidls[index] = null;
					}
				}
				finally
				{
					for (var index = 0; index < fetchedCount; index++)
					{
						if (childPidls[index] is not null)
						{
							PInvoke.CoTaskMemFree(childPidls[index]);
							childPidls[index] = null;
						}
					}
				}
			}

			if (batch.Count > 0)
			{
				batchCount++;
				var channelWriteStartTimestamp = Stopwatch.GetTimestamp();
				if (!WriteBatch(writer, batch, cancellationToken))
				{
					return false;
				}

				channelWriteDuration += Stopwatch.GetElapsedTime(channelWriteStartTimestamp);
			}

			writer.TryComplete();
			CoreDiagnosticLog.Write(
				"WindowsStorableFactory",
				$"EnumerateOnSTA END batches={batchCount} items={itemCount} nextCalls={nextCallCount} nextMs={nextDuration.TotalMilliseconds:F1} " +
				$"descriptorMs={descriptorDuration.TotalMilliseconds:F1} channelWriteMs={channelWriteDuration.TotalMilliseconds:F1} " +
				$"elapsedMs={Stopwatch.GetElapsedTime(enumerationStartTimestamp).TotalMilliseconds:F1}");

			return true;
		}
		catch (Exception exception)
		{
			CoreDiagnosticLog.Write(
				"WindowsStorableFactory",
				$"EnumerateOnSTA ERROR type={exception.GetType().Name} message={exception.Message} elapsedMs={Stopwatch.GetElapsedTime(enumerationStartTimestamp).TotalMilliseconds:F1}");
			writer.TryComplete(exception);
			throw;
		}
	}

	private static bool WriteBatch(ChannelWriter<IReadOnlyList<WindowsStorableDescriptorData>> writer, IReadOnlyList<WindowsStorableDescriptorData> batch, CancellationToken cancellationToken)
	{
		while (!writer.TryWrite(batch))
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return false;
			}

			if (!writer.WaitToWriteAsync().AsTask().GetAwaiter().GetResult())
			{
				return false;
			}
		}

		return true;
	}

	private static async Task CompleteChannelWhenFinishedAsync(Task<bool> producer, ChannelWriter<IReadOnlyList<WindowsStorableDescriptorData>> writer)
	{
		try
		{
			await producer.ConfigureAwait(false);
			writer.TryComplete();
		}
		catch (Exception exception)
		{
			writer.TryComplete(exception);
			throw;
		}
	}

	private static void ThrowIfEnumerationFailed(HRESULT hr)
	{
		if (hr.Value is CanceledHResultValue)
		{
			throw new OperationCanceledException("The Windows Shell canceled folder enumeration.");
		}

		hr.ThrowOnFailure();
	}

	private static bool IsMatchingItem(WindowsStorable? storable, string itemId)
	{
		return storable is not null && StringComparer.Ordinal.Equals(storable.Id, itemId);
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Files.Core.Diagnostics;
using OwlCore.Storage;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Resolves Shell interfaces on the ordered STA lane and returns managed models or affine wrappers.
/// </summary>
internal sealed class WindowsStorableFactory
{
	private const int EnumerationBatchSize = 32;

	private const int EnumerationBufferSize = 4;

	private const int IdentityWorkerCount = 4;

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
				var result = PInvoke.SHGetKnownFolderItem(knownFolderId, KNOWN_FOLDER_FLAG.KF_FLAG_DEFAULT, null, out IShellItem shellItem);
				result.ThrowOnFailure();

				return Create(ShellItemHelpers.CreateDescriptor(shellItem, _itemIdReader));
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
				var parentResult = shellItem.GetParent(out var parent);

				if (parentResult.Failed)
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
			shellItem => WindowsShellColumnReader.Read(shellItem, descriptor.Locator.ParsingName),
			cancellationToken);
	}

	internal async IAsyncEnumerable<WindowsStorableDescriptor> EnumerateChildrenAsync(WindowsStorableDescriptor descriptor, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(descriptor);

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
			var scheduledProducer = _resolver.InvokeConcurrentAsync(
				descriptor.Locator,
				shellItem => EnumerateChildrenOnCurrentSta(shellItem, batches.Writer, enumerationCancellation.Token),
				enumerationCancellation.Token);
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

			CoreDiagnosticLog.Write("WindowsStorableFactory", $"Enumerate END name={descriptor.Snapshot.Name} batches={batchCount} items={itemCount} identityMs={identityDuration.TotalMilliseconds:F1} elapsedMs={Stopwatch.GetElapsedTime(enumerationStartTimestamp).TotalMilliseconds:F1}");
		}
	}

	public Task<Stream> OpenStreamAsync(WindowsStorableDescriptor descriptor, FileAccess accessMode, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(descriptor);

		return _resolver.InvokeAsync<Stream>(
			descriptor.Locator,
			shellItem =>
			{
				var bindContextResult = PInvoke.CreateBindCtx(0, out IBindCtx? bindContext);
				bindContextResult.ThrowOnFailure();

				if (bindContext is null)
				{
					throw new IOException("Could not create a Shell bind context.");
				}

				var bindOptions = new BIND_OPTS
				{
					cbStruct = (uint)Unsafe.SizeOf<BIND_OPTS>(),
					grfMode = accessMode switch
					{
						FileAccess.Read => (uint)(STGM.STGM_READ | STGM.STGM_SHARE_DENY_NONE),
						FileAccess.Write => (uint)(STGM.STGM_WRITE | STGM.STGM_SHARE_DENY_WRITE),
						FileAccess.ReadWrite => (uint)(STGM.STGM_READWRITE | STGM.STGM_SHARE_DENY_WRITE),
						_ => throw new ArgumentOutOfRangeException(nameof(accessMode)),
					},
				};
				bindContext.SetBindOptions(bindOptions).ThrowOnFailure();

				var bindResult = shellItem.BindToHandler(bindContext, PInvoke.BHID_Stream, out IStream? shellStream);
				bindResult.ThrowOnFailure();

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

	private static unsafe bool EnumerateChildrenOnCurrentSta(IShellItem shellItem, ChannelWriter<IReadOnlyList<WindowsStorableDescriptorData>> writer, CancellationToken cancellationToken)
	{
		var enumerationStartTimestamp = Stopwatch.GetTimestamp();
		var batchCount = 0;
		var itemCount = 0;
		var nextCallCount = 0;
		var nextDuration = TimeSpan.Zero;
		var descriptorDuration = TimeSpan.Zero;
		var channelWriteDuration = TimeSpan.Zero;
		CoreDiagnosticLog.Write("WindowsStorableFactory", "EnumerateOnSTA START");
		try
		{
			var bindResult = shellItem.BindToHandler(null, PInvoke.BHID_EnumItems, out IEnumShellItems? enumerator);
			bindResult.ThrowOnFailure();

			if (enumerator is null)
			{
				throw new InvalidOperationException("The Shell folder returned no item enumerator.");
			}

			var batch = new List<WindowsStorableDescriptorData>(EnumerationBatchSize);
			var children = new IShellItem[EnumerationBatchSize];

			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var nextStartTimestamp = Stopwatch.GetTimestamp();
				uint fetched = 0;
				var result = enumerator.Next((uint)children.Length, children, &fetched);
				nextCallCount++;
				nextDuration += Stopwatch.GetElapsedTime(nextStartTimestamp);

				if (result == HRESULT.S_FALSE && fetched is 0)
				{
					break;
				}

				result.ThrowOnFailure();
				if (fetched is 0)
				{
					break;
				}

				var fetchedCount = checked((int)fetched);
				for (var index = 0; index < fetchedCount; index++)
				{
					var child = children[index];
					children[index] = null!;
					var descriptorStartTimestamp = Stopwatch.GetTimestamp();
					batch.Add(ShellItemHelpers.CreateDescriptorData(child));
					descriptorDuration += Stopwatch.GetElapsedTime(descriptorStartTimestamp);
					itemCount++;

					if (batch.Count >= EnumerationBatchSize)
					{
						batchCount++;
						var channelWriteStartTimestamp = Stopwatch.GetTimestamp();
						WriteBatch(writer, batch, cancellationToken);
						channelWriteDuration += Stopwatch.GetElapsedTime(channelWriteStartTimestamp);
						batch = new List<WindowsStorableDescriptorData>(EnumerationBatchSize);
					}
				}

				for (var index = fetchedCount; index < children.Length; index++)
				{
					children[index] = null!;
				}
			}

			if (batch.Count > 0)
			{
				batchCount++;
				var channelWriteStartTimestamp = Stopwatch.GetTimestamp();
				WriteBatch(writer, batch, cancellationToken);
				channelWriteDuration += Stopwatch.GetElapsedTime(channelWriteStartTimestamp);
			}

			writer.TryComplete();
			CoreDiagnosticLog.Write("WindowsStorableFactory", $"EnumerateOnSTA END batches={batchCount} items={itemCount} nextCalls={nextCallCount} nextMs={nextDuration.TotalMilliseconds:F1} descriptorMs={descriptorDuration.TotalMilliseconds:F1} channelWriteMs={channelWriteDuration.TotalMilliseconds:F1} elapsedMs={Stopwatch.GetElapsedTime(enumerationStartTimestamp).TotalMilliseconds:F1}");

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

	private static void WriteBatch(ChannelWriter<IReadOnlyList<WindowsStorableDescriptorData>> writer, IReadOnlyList<WindowsStorableDescriptorData> batch, CancellationToken cancellationToken)
	{
		writer.WriteAsync(batch, cancellationToken).AsTask().GetAwaiter().GetResult();
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

	private static bool IsMatchingItem(WindowsStorable? storable, string itemId)
	{
		return storable is not null && StringComparer.Ordinal.Equals(storable.Id, itemId);
	}
}

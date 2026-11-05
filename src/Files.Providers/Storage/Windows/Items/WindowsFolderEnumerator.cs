// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Storage.Windows;

/// <summary>Enumerates Shell children on their STA lane and builds apartment-neutral descriptors.</summary>
internal sealed class WindowsFolderEnumerator
{
	private const int EnumerationBatchSize = 32;
	private const int EnumerationBufferSize = 4;
	private const int IdentityWorkerCount = 4;
	private const int SearchEnumerationBatchSize = 1;

	private readonly IWindowsShellScheduler _scheduler;
	private readonly IWindowsItemIdReader _itemIdReader;
	private readonly ILogger<WindowsFolderEnumerator> _logger;

	internal WindowsFolderEnumerator(IWindowsShellScheduler scheduler, IWindowsItemIdReader itemIdReader, ILogger<WindowsFolderEnumerator>? logger = null)
	{
		ArgumentNullException.ThrowIfNull(scheduler);
		ArgumentNullException.ThrowIfNull(itemIdReader);

		_scheduler = scheduler;
		_itemIdReader = itemIdReader;
		_logger = logger ?? NullLogger<WindowsFolderEnumerator>.Instance;
	}

	internal async IAsyncEnumerable<WindowsStorableDescriptor> EnumerateChildrenAsync(
		WindowsStorableDescriptor descriptor,
		HWND ownerWindow,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(descriptor);

		var parentFolder = new WindowsItemLocator(descriptor.Locator.AbsolutePidl, descriptor.Locator.ParsingName);
		var enumerationStartTimestamp = Stopwatch.GetTimestamp();
		var batchCount = 0;
		var itemCount = 0;
		var identityDuration = TimeSpan.Zero;
		if (_logger.IsEnabled(LogLevel.Debug))
		{
			_logger.LogDebug("{Component}: {Message}", "WindowsFolderEnumerator", $"Enumerate START name={descriptor.Metadata.Name} parsingName={descriptor.Locator.ParsingName}");
		}

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
			Func<bool> enumerateChildren = () =>
			{
				var shellItem = WindowShellHelpers.TryResolveItem(descriptor.Locator);

				return shellItem is not null && EnumerateChildrenOnCurrentSta(shellItem, parentFolder, descriptor.IsSearchFolder, ownerWindow, batches.Writer, _logger, enumerationCancellation.Token);
			};

			var scheduledProducer = descriptor.IsSearchFolder
				? _scheduler.InvokeSearchAsync(enumerateChildren, enumerationCancellation.Token)
				: _scheduler.InvokeConcurrentAsync(enumerateChildren, enumerationCancellation.Token);
			producer = scheduledProducer.ContinueWith(
				static (task, state) =>
				{
					var writer = (ChannelWriter<IReadOnlyList<WindowsStorableDescriptorData>>)state!;
					try
					{
						task.GetAwaiter().GetResult();
						writer.TryComplete();
					}
					catch (Exception exception)
					{
						writer.TryComplete(exception);
						throw;
					}
				},
				batches.Writer,
				CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);

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
				catch (OperationCanceledException) when (enumerationCancellation.IsCancellationRequested)
				{
				}
			}

			if (_logger.IsEnabled(LogLevel.Debug))
			{
				_logger.LogDebug(
					"{Component}: {Message}",
					"WindowsFolderEnumerator",
					$"Enumerate END name={descriptor.Metadata.Name} batches={batchCount} items={itemCount} " +
					$"identityMs={identityDuration.TotalMilliseconds:F1} elapsedMs={Stopwatch.GetElapsedTime(enumerationStartTimestamp).TotalMilliseconds:F1}"
				);
			}
		}
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

		Parallel.For(0, items.Count, parallelOptions, index => descriptors[index] = WindowShellHelpers.CreateDescriptor(items[index], _itemIdReader));

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

	private static unsafe bool EnumerateChildrenOnCurrentSta(
		IShellItem shellItem,
		WindowsItemLocator parentFolder,
		bool isSearchFolder,
		HWND ownerWindow,
		ChannelWriter<IReadOnlyList<WindowsStorableDescriptorData>> writer,
		ILogger<WindowsFolderEnumerator> logger,
		CancellationToken cancellationToken)
	{
		var enumerationStartTimestamp = Stopwatch.GetTimestamp();
		var batchCount = 0;
		var itemCount = 0;
		var nextCallCount = 0;
		var nextDuration = TimeSpan.Zero;
		var descriptorDuration = TimeSpan.Zero;
		var channelWriteDuration = TimeSpan.Zero;
		if (logger.IsEnabled(LogLevel.Debug))
		{
			logger.LogDebug("{Component}: {Message}", "WindowsFolderEnumerator", "EnumerateOnSTA START");
		}

		using var cancellationRegistration = cancellationToken.UnsafeRegister(static state => ((ChannelWriter<IReadOnlyList<WindowsStorableDescriptorData>>)state!).TryComplete(), writer);

		try
		{
			var folder = WindowsShellColumnReader.TryGetFolder(shellItem, parentFolder.ParsingName, cancellationToken)
				?? throw new InvalidOperationException("The Shell item returned no folder interface.");
			var enumerationFlags = _SHCONTF.SHCONTF_FOLDERS | _SHCONTF.SHCONTF_NONFOLDERS | _SHCONTF.SHCONTF_INCLUDEHIDDEN;
			var hr = folder.EnumObjects(ownerWindow, (uint)enumerationFlags, out IEnumIDList? enumerator);
			if (hr == HRESULT.S_FALSE)
			{
				writer.TryComplete();

				return true;
			}

			if (hr.Value == HRESULT.FromWin32Error(WIN32_ERROR.ERROR_CANCELLED))
			{
				throw new OperationCanceledException("The Windows Shell canceled folder enumeration.");
			}

			hr.ThrowOnFailure();

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
					if (logger.IsEnabled(LogLevel.Debug))
					{
						logger.LogDebug(
							"{Component}: {Message}",
							"WindowsFolderEnumerator",
							$"EnumerateOnSTA CANCELLED batches={batchCount} items={itemCount} elapsedMs={Stopwatch.GetElapsedTime(enumerationStartTimestamp).TotalMilliseconds:F1}"
						);
					}

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

				if (hr.Value == HRESULT.FromWin32Error(WIN32_ERROR.ERROR_CANCELLED))
				{
					throw new OperationCanceledException("The Windows Shell canceled folder enumeration.");
				}

				hr.ThrowOnFailure();
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

						var relativePidl = WindowShellHelpers.CopyPidl(childPidl);
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
						batch.Add(WindowShellHelpers.CreateDescriptorData(child!, parentFolder, absolutePidl, relativePidl, itemStoreReference));
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
			if (logger.IsEnabled(LogLevel.Debug))
			{
				logger.LogDebug(
					"{Component}: {Message}",
					"WindowsFolderEnumerator",
					$"EnumerateOnSTA END batches={batchCount} items={itemCount} nextCalls={nextCallCount} nextMs={nextDuration.TotalMilliseconds:F1} " +
					$"descriptorMs={descriptorDuration.TotalMilliseconds:F1} channelWriteMs={channelWriteDuration.TotalMilliseconds:F1} " +
					$"elapsedMs={Stopwatch.GetElapsedTime(enumerationStartTimestamp).TotalMilliseconds:F1}"
				);
			}

			return true;
		}
		catch (Exception exception)
		{
			if (logger.IsEnabled(LogLevel.Debug))
			{
				logger.LogDebug(
					"{Component}: {Message}",
					"WindowsFolderEnumerator",
					$"EnumerateOnSTA ERROR type={exception.GetType().Name} message={exception.Message} elapsedMs={Stopwatch.GetElapsedTime(enumerationStartTimestamp).TotalMilliseconds:F1}"
				);
			}

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

			if (!writer.WaitToWriteAsync(cancellationToken).AsTask().GetAwaiter().GetResult())
			{
				return false;
			}
		}

		return true;
	}

}

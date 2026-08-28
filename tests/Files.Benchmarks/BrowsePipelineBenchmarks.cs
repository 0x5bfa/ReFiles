// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Files.Core.Browsing;
using Files.Core.Capabilities;
using Files.Core.Models;
using Files.Core.Storage;
using OwlCore.Storage;

namespace Files.Benchmarks;

/// <summary>
/// Measures browse-session navigation performance across different item counts.
/// </summary>
[MemoryDiagnoser]
public class BrowsePipelineBenchmarks
{
	/// <summary>
	/// Gets or sets the number of items generated for the benchmark.
	/// </summary>
	[Params(100, 1_000, 10_000, 44_000)]
	public int ItemCount { get; set; }

	/// <summary>
	/// Measures the time required to navigate to a generated home location.
	/// </summary>
	/// <returns>A task that represents the asynchronous navigation operation.</returns>
	[Benchmark]
	public async Task NavigateAsync()
	{
		await using var session = new BrowseSession(new BenchmarkBrowseLocationResolver(ItemCount));
		await session.NavigateAsync(HomeLocation.Instance);
	}
}

internal static class BrowsePipelineScenarioRunner
{
	public static async Task<BrowsePipelineScenarioResult> RunAsync(int itemCount)
	{
		var resolver = new BenchmarkBrowseLocationResolver(itemCount);
		await using var session = new BrowseSession(resolver);
		var startTimestamp = Stopwatch.GetTimestamp();
		var firstBatchTimestamp = 0L;
		var notificationCount = 0;
		session.ItemsChanged += (_, _) =>
		{
			Interlocked.CompareExchange(ref firstBatchTimestamp, Stopwatch.GetTimestamp(), 0);
			Interlocked.Increment(ref notificationCount);
		};
		var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);

		await session.NavigateAsync(HomeLocation.Instance);

		var completedTimestamp = Stopwatch.GetTimestamp();
		var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

		return new BrowsePipelineScenarioResult(
			itemCount,
			GetElapsedMilliseconds(startTimestamp, resolver.FirstItemTimestamp),
			GetElapsedMilliseconds(startTimestamp, Volatile.Read(ref firstBatchTimestamp)),
			GetElapsedMilliseconds(startTimestamp, completedTimestamp),
			allocatedBytes,
			Volatile.Read(ref notificationCount));
	}

	private static double GetElapsedMilliseconds(long startTimestamp, long endTimestamp)
	{
		return endTimestamp is 0
			? double.NaN
			: Stopwatch.GetElapsedTime(startTimestamp, endTimestamp).TotalMilliseconds;
	}
}

internal sealed record BrowsePipelineScenarioResult(
	int ItemCount,
	double FirstItemMilliseconds,
	double FirstBatchMilliseconds,
	double TotalMilliseconds,
	long AllocatedBytes,
	int NotificationCount);

internal sealed class BenchmarkBrowseLocationResolver(int itemCount) : IBrowseLocationResolver
{
	private readonly int _itemCount = itemCount;
	private readonly BrowseBenchmarkStorageSource _source = new();
	private long _firstItemTimestamp;

	public long FirstItemTimestamp => Volatile.Read(ref _firstItemTimestamp);

	public ValueTask<IBrowseLocationContext> OpenAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(location);
		cancellationToken.ThrowIfCancellationRequested();

		return ValueTask.FromResult<IBrowseLocationContext>(
			new BenchmarkBrowseLocationContext(location, _itemCount, _source, timestamp => Interlocked.CompareExchange(ref _firstItemTimestamp, timestamp, 0)));
	}
}

internal sealed class BenchmarkBrowseLocationContext(BrowseLocation location, int itemCount, BrowseBenchmarkStorageSource source, Action<long> reportFirstItem) : IBrowseLocationContext
{
	private readonly int _itemCount = itemCount;
	private readonly BrowseBenchmarkStorageSource _source = source;
	private readonly Action<long> _reportFirstItem = reportFirstItem;

	public BrowseLocation Location { get; } = location;

	public IStorableModel? LocationModel => null;

	public async IAsyncEnumerable<IStorableModel> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		for (var index = 0; index < _itemCount; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var coreModel = new BrowseBenchmarkStorable($"item-{index:D5}", $"Item {index:D5}");
			var reference = new StorableReference(_source.SourceId, coreModel.Id, new StorageAddress("benchmark", coreModel.Id));
			var context = new ItemContext(_source, coreModel, reference);
			var model = new StorableModel(coreModel, reference, CapabilityRegistry.Empty.CreateCapabilities(context));
			_reportFirstItem(Stopwatch.GetTimestamp());

			yield return model;
			if ((index + 1) % 32 is 0)
			{
				await Task.Yield();
			}
		}
	}

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class BrowseBenchmarkStorageSource : IStorageSource
{
	public StorageSourceId SourceId { get; } = new("benchmark");

	public string SourceType => "benchmark";

	public string DisplayName => "Benchmark";

	public async IAsyncEnumerable<IFolder> GetRootsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		await Task.CompletedTask;
		yield break;
	}

	public bool CanResolve(StorageAddress address) => false;

	public ValueTask<IStorable> ResolveAsync(StorageAddress address, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public ValueTask<IStorable> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class BrowseBenchmarkStorable(string id, string name) : IStorable
{
	public string Id { get; } = id;

	public string Name { get; } = name;
}

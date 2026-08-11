// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using BenchmarkDotNet.Attributes;
using Files.Core.ItemFeatures;
using Files.Core.Storage;
using OwlCore.Storage;

namespace Files.Benchmarks;

/// <summary>
/// Measures cold and cached item-feature resolution performance.
/// </summary>
[MemoryDiagnoser]
public class ItemFeatureResolutionBenchmarks
{
	private ItemFeatureRegistry registry = null!;

	private ItemContext context = null!;

	private IItemFeatures cachedFeatures = null!;

	/// <summary>
	/// Gets or sets the number of feature factories registered for the benchmark.
	/// </summary>
	[Params(1, 4, 16)]
	public int FactoryCount { get; set; }

	/// <summary>
	/// Creates the feature registry and warms the cached resolution path.
	/// </summary>
	[GlobalSetup]
	public void Setup()
	{
		var source = new BenchmarkStorageSource();
		var coreModel = new BenchmarkStorable("item", "Item");
		var reference = new StorableReference(source.SourceId, coreModel.Id);
		context = new ItemContext(source, coreModel, reference);

		var builder = new ItemFeatureBuilder();
		for (var index = 0; index < FactoryCount; index++)
		{
			var value = index.ToString();
			builder.Add<BenchmarkFeature>(new DelegateItemFeatureFactory<BenchmarkFeature>(_ => new BenchmarkFeature(value)), priority: index);
		}

		registry = builder
			.SetCombiner<BenchmarkFeature>(new PriorityItemFeatureCombiner<BenchmarkFeature>())
			.Build();
		cachedFeatures = registry.CreateFeatures(context);
		_ = cachedFeatures.Get<BenchmarkFeature>();
	}

	/// <summary>
	/// Releases the cached feature set after the benchmark completes.
	/// </summary>
	[GlobalCleanup]
	public void Cleanup() => cachedFeatures.Dispose();

	/// <summary>
	/// Measures resolving a feature from a newly created feature set.
	/// </summary>
	/// <returns>The resolved feature value.</returns>
	[Benchmark(Baseline = true)]
	public string ColdResolution()
	{
		using var features = registry.CreateFeatures(context);

		return features.Get<BenchmarkFeature>()!.Value;
	}

	/// <summary>
	/// Measures resolving a feature from the cached feature set.
	/// </summary>
	/// <returns>The resolved feature value.</returns>
	[Benchmark]
	public string CachedResolution() => cachedFeatures.Get<BenchmarkFeature>()!.Value;
}

internal sealed class BenchmarkFeature
{
	public string Value { get; }

	public BenchmarkFeature(string value) => Value = value;
}

internal sealed class BenchmarkStorable : IStorable
{
	public string Id { get; }

	public string Name { get; }

	public BenchmarkStorable(string id, string name)
	{
		Id = id;
		Name = name;
	}
}

internal sealed class BenchmarkStorageSource : IStorageSource
{
	public StorageSourceId SourceId { get; } = new("benchmark");

	public string SourceType => "benchmark";

	public string DisplayName => "Benchmark";

	public async IAsyncEnumerable<IFolder> GetRootsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		await Task.CompletedTask.ConfigureAwait(false);
		yield break;
	}

	public bool CanResolve(StorageAddress address) => false;

	public ValueTask<IStorable> ResolveAsync(StorageAddress address, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public ValueTask<IStorable> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using BenchmarkDotNet.Attributes;
using Files.Core.ItemFeatures;
using Files.Core.Storage;
using OwlCore.Storage;

namespace Files.Benchmarks;

[MemoryDiagnoser]
public class ItemFeatureResolutionBenchmarks
{
	[Params(1, 4, 16)]
	public int FactoryCount { get; set; }

	private ItemFeatureRegistry registry = null!;
	private ItemContext context = null!;
	private IItemFeatures cachedFeatures = null!;

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

	[GlobalCleanup]
	public void Cleanup() => cachedFeatures.Dispose();

	[Benchmark(Baseline = true)]
	public string ColdResolution()
	{
		using var features = registry.CreateFeatures(context);
		return features.Get<BenchmarkFeature>()!.Value;
	}

	[Benchmark]
	public string CachedResolution() => cachedFeatures.Get<BenchmarkFeature>()!.Value;
}

internal sealed class BenchmarkFeature
{
	public BenchmarkFeature(string value) => Value = value;

	public string Value { get; }
}

internal sealed class BenchmarkStorable : IStorable
{
	public BenchmarkStorable(string id, string name)
	{
		Id = id;
		Name = name;
	}

	public string Id { get; }

	public string Name { get; }
}

internal sealed class BenchmarkStorageSource : IStorageSource
{
	public StorageSourceId SourceId { get; } = new("benchmark");

	public string SourceType => "benchmark";

	public string DisplayName => "Benchmark";

	public async IAsyncEnumerable<IFolder> GetRootsAsync(
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
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

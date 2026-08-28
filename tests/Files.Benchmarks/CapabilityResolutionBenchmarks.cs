// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using BenchmarkDotNet.Attributes;
using Files.Core.Capabilities;
using Files.Core.Storage;
using OwlCore.Storage;

namespace Files.Benchmarks;

/// <summary>
/// Measures cold and cached item-capability resolution performance.
/// </summary>
[MemoryDiagnoser]
public class CapabilityResolutionBenchmarks
{
	private CapabilityRegistry registry = null!;

	private ItemContext context = null!;

	private ICapabilities cachedCapabilities = null!;

	/// <summary>
	/// Gets or sets the number of capability factories registered for the benchmark.
	/// </summary>
	[Params(1, 4, 16)]
	public int FactoryCount { get; set; }

	/// <summary>
	/// Creates the capability registry and warms the cached resolution path.
	/// </summary>
	[GlobalSetup]
	public void Setup()
	{
		var source = new BenchmarkStorageSource();
		var coreModel = new BenchmarkStorable("item", "Item");
		var reference = new StorableReference(source.SourceId, coreModel.Id);
		context = new ItemContext(source, coreModel, reference);

		var builder = new CapabilityBuilder();
		for (var index = 0; index < FactoryCount; index++)
		{
			var value = index.ToString();
			builder.Add<BenchmarkCapability>(new DelegateCapabilityFactory<BenchmarkCapability>(_ => new BenchmarkCapability(value)), priority: index);
		}

		registry = builder
			.SetCombiner<BenchmarkCapability>(new PriorityCapabilityCombiner<BenchmarkCapability>())
			.Build();
		cachedCapabilities = registry.CreateCapabilities(context);
		_ = cachedCapabilities.Get<BenchmarkCapability>();
	}

	/// <summary>
	/// Releases the cached capability set after the benchmark completes.
	/// </summary>
	[GlobalCleanup]
	public void Cleanup() => cachedCapabilities.Dispose();

	/// <summary>
	/// Measures resolving a capability from a newly created capability set.
	/// </summary>
	/// <returns>The resolved capability value.</returns>
	[Benchmark(Baseline = true)]
	public string ColdResolution()
	{
		using var capabilities = registry.CreateCapabilities(context);

		return capabilities.Get<BenchmarkCapability>()!.Value;
	}

	/// <summary>
	/// Measures resolving a capability from the cached capability set.
	/// </summary>
	/// <returns>The resolved capability value.</returns>
	[Benchmark]
	public string CachedResolution() => cachedCapabilities.Get<BenchmarkCapability>()!.Value;
}

internal sealed class BenchmarkCapability
{
	public string Value { get; }

	public BenchmarkCapability(string value) => Value = value;
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

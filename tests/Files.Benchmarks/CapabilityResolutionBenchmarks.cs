// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using BenchmarkDotNet.Attributes;
using Files.Core.Capabilities;
using Files.Core.Storage;

namespace Files.Benchmarks;

/// <summary>
/// Measures cold and cached item-capability resolution performance.
/// </summary>
[MemoryDiagnoser]
public class CapabilityResolutionBenchmarks
{
	private CapabilityRegistry _registry = null!;

	private ItemContext _context = null!;

	private IOwnedCapabilities _cachedCapabilities = null!;

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
		_context = new ItemContext(source, coreModel, reference);

		var builder = new CapabilityBuilder();
		for (var index = 0; index < FactoryCount; index++)
		{
			var value = index.ToString();
			builder.Add<IBenchmarkCapability>(new DelegateCapabilityFactory<IBenchmarkCapability>(_ => new BenchmarkCapability(value)), priority: index);
		}

		_registry = builder
			.SetBorrowedView<IBenchmarkCapability>(static inner => new BenchmarkCapability(inner.Value))
			.SetCombiner<IBenchmarkCapability>(new PriorityCapabilityCombiner<IBenchmarkCapability>())
			.Build();
		_cachedCapabilities = _registry.CreateCapabilities(_context);
		_ = _cachedCapabilities.Get<IBenchmarkCapability>();
	}

	/// <summary>
	/// Releases the cached capability set after the benchmark completes.
	/// </summary>
	[GlobalCleanup]
	public void Cleanup()
	{
		_cachedCapabilities.Dispose();
	}

	/// <summary>
	/// Measures resolving a capability from a newly created capability set.
	/// </summary>
	/// <returns>The resolved capability value.</returns>
	[Benchmark(Baseline = true)]
	public string ColdResolution()
	{
		using var capabilities = _registry.CreateCapabilities(_context);

		return capabilities.Get<IBenchmarkCapability>()!.Value;
	}

	/// <summary>
	/// Measures resolving a capability from the cached capability set.
	/// </summary>
	/// <returns>The resolved capability value.</returns>
	[Benchmark]
	public string CachedResolution()
	{
		return _cachedCapabilities.Get<IBenchmarkCapability>()!.Value;
	}
}

internal interface IBenchmarkCapability : ICapability
{
	string Value { get; }
}

internal sealed class BenchmarkCapability : IBenchmarkCapability
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

	public bool CanResolve(StorageAddress address)
	{
		return false;
	}

	public ValueTask<IStorable> ResolveAsync(StorageAddress address, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public ValueTask<IStorable> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}
}

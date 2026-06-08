// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;

namespace Files.Core.ItemFeatures;

internal sealed class ItemFeatures : IItemFeatures
{
	private static readonly object MissingFeature = new();

	private readonly object syncRoot = new();
	private readonly ItemFeatureRegistry registry;
	private readonly ItemContext context;
	private readonly Dictionary<Type, object> resolvedFeatures = [];
	private readonly List<object> ownedInstances = [];
	private Task? disposeTask;
	private bool isDisposed;

	public ItemFeatures(ItemFeatureRegistry registry, ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(registry);
		ArgumentNullException.ThrowIfNull(context);

		this.registry = registry;
		this.context = context;
	}

	public TFeature? Get<TFeature>()
		where TFeature : class
	{
		lock (syncRoot)
		{
			ObjectDisposedException.ThrowIf(isDisposed, this);

			if (resolvedFeatures.TryGetValue(typeof(TFeature), out var cached))
			{
				return ReferenceEquals(cached, MissingFeature)
					? null
					: (TFeature)cached;
			}

			var resolution = registry.Resolve<TFeature>(context);

			foreach (var instance in resolution.OwnedInstances)
			{
				if (!ownedInstances.Any(existing => ReferenceEquals(existing, instance)))
				{
					ownedInstances.Add(instance);
				}
			}

			resolvedFeatures.Add(
				typeof(TFeature),
				resolution.Feature ?? MissingFeature);

			return resolution.Feature;
		}
	}

	public bool TryGet<TFeature>([NotNullWhen(true)] out TFeature? feature)
		where TFeature : class
	{
		feature = Get<TFeature>();
		return feature is not null;
	}

	public void Dispose()
	{
		DisposeAsync().AsTask().GetAwaiter().GetResult();
	}

	public ValueTask DisposeAsync()
	{
		lock (syncRoot)
		{
			if (disposeTask is not null)
			{
				return new ValueTask(disposeTask);
			}

			isDisposed = true;
			var instances = ownedInstances.ToArray();
			ownedInstances.Clear();
			resolvedFeatures.Clear();
			disposeTask = DisposeInstancesAsync(instances);
			GC.SuppressFinalize(this);
			return new ValueTask(disposeTask);
		}
	}

	internal static async Task DisposeInstancesAsync(IEnumerable<object> instances)
	{
		List<Exception>? exceptions = null;

		foreach (var instance in instances.Reverse())
		{
			try
			{
				if (instance is IAsyncDisposable asyncDisposable)
				{
					await asyncDisposable.DisposeAsync().ConfigureAwait(false);
				}
				else
				{
					(instance as IDisposable)?.Dispose();
				}
			}
			catch (Exception exception)
			{
				(exceptions ??= []).Add(exception);
			}
		}

		if (exceptions is not null)
		{
			throw new AggregateException("One or more item features could not be disposed.", exceptions);
		}
	}
}

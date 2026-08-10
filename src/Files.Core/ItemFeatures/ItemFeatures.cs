// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;

namespace Files.Core.ItemFeatures;

internal sealed class ItemFeatures : IItemFeatures
{
	private static readonly object _missingFeature = new();

	private readonly ItemFeatureRegistry _registry;
	private readonly ItemContext _context;
	private Lock? _syncRoot;
	private Dictionary<Type, object>? _resolvedFeatures;
	private List<object>? _ownedInstances;
	private Task? _disposeTask;
	private bool _isDisposed;

	public ItemFeatures(ItemFeatureRegistry registry, ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(registry);
		ArgumentNullException.ThrowIfNull(context);

		_registry = registry;
		_context = context;
	}

	public TFeature? Get<TFeature>()
		where TFeature : class
	{
		lock (GetSyncRoot())
		{
			ObjectDisposedException.ThrowIf(_isDisposed, this);

			if (_resolvedFeatures?.TryGetValue(typeof(TFeature), out var cached) is true)
			{
				return ReferenceEquals(cached, _missingFeature)
					? null
					: (TFeature)cached;
			}

			var resolution = _registry.Resolve<TFeature>(_context);

			if (resolution.OwnedInstances.Count is not 0)
			{
				_ownedInstances ??= [];
				foreach (var instance in resolution.OwnedInstances)
				{
					if (!_ownedInstances.Any(existing => ReferenceEquals(existing, instance)))
					{
						_ownedInstances.Add(instance);
					}
				}
			}

			(_resolvedFeatures ??= [])[typeof(TFeature)] = resolution.Feature ?? _missingFeature;

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
		lock (GetSyncRoot())
		{
			if (_disposeTask is not null)
			{
				return new ValueTask(_disposeTask);
			}

			_isDisposed = true;
			var instances = _ownedInstances?.ToArray() ?? [];
			_ownedInstances?.Clear();
			_resolvedFeatures?.Clear();
			_disposeTask = DisposeInstancesAsync(instances);
			GC.SuppressFinalize(this);

			return new ValueTask(_disposeTask);
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

	private Lock GetSyncRoot()
	{
		var syncRoot = Volatile.Read(ref _syncRoot);
		if (syncRoot is not null)
		{
			return syncRoot;
		}

		var createdSyncRoot = new Lock();
		var existingSyncRoot = Interlocked.CompareExchange(ref _syncRoot, createdSyncRoot, null);

		return existingSyncRoot ?? createdSyncRoot;
	}
}

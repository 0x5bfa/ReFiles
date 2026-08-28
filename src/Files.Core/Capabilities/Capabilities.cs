// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;

namespace Files.Core.Capabilities;

internal sealed class Capabilities : ICapabilities
{
	private static readonly object _missingCapability = new();

	private readonly CapabilityRegistry _registry;
	private readonly ItemContext _context;
	private Lock? _syncRoot;
	private Dictionary<Type, object>? _resolvedCapabilities;
	private List<object>? _ownedInstances;
	private Task? _disposeTask;
	private bool _isDisposed;

	public Capabilities(CapabilityRegistry registry, ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(registry);
		ArgumentNullException.ThrowIfNull(context);

		_registry = registry;
		_context = context;
	}

	public TCapability? Get<TCapability>()
		where TCapability : class
	{
		lock (GetSyncRoot())
		{
			ObjectDisposedException.ThrowIf(_isDisposed, this);

			if (_resolvedCapabilities?.TryGetValue(typeof(TCapability), out var cached) is true)
			{
				return ReferenceEquals(cached, _missingCapability)
					? null
					: (TCapability)cached;
			}

			var resolution = _registry.Resolve<TCapability>(_context);

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

			(_resolvedCapabilities ??= [])[typeof(TCapability)] = resolution.Capability ?? _missingCapability;

			return resolution.Capability;
		}
	}

	public bool TryGet<TCapability>([NotNullWhen(true)] out TCapability? capability)
		where TCapability : class
	{
		capability = Get<TCapability>();

		return capability is not null;
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
			_resolvedCapabilities?.Clear();
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
			throw new AggregateException("One or more item capabilities could not be disposed.", exceptions);
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

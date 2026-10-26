// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Files.Core.Capabilities.Changes;
using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Storage;

namespace Files.Core.Browsing;

/// <summary>Aggregates independently loaded source roots without creating a synthetic storable.</summary>
public sealed class HomeBrowseLocationContext : IBrowseLocationContext, IBrowseLocationItemResolver, IFolderChangeSource
{
	private readonly IStorageWorkspace _workspace;
	private readonly Lock _syncRoot = new();
	private readonly CancellationTokenSource _lifetime = new();
	private readonly SourceEntry[] _sources;
	private readonly List<Task> _work = [];
	private Task? _disposeTask;
	private bool _enumerationStarted;
	private bool _isDisposed;

	/// <inheritdoc />
	public BrowseLocation Location { get; }

	/// <inheritdoc />
	public IStorableModel? LocationModel => null;

	/// <inheritdoc />
	public IFolderChangeSource ChangeSource => this;

	/// <summary>Gets immutable loading states for the sources in this context.</summary>
	public IReadOnlyList<HomeSourceState> Sources
	{
		get
		{
			lock (_syncRoot)
			{
				return Array.AsReadOnly(_sources.Select(entry => entry.State).ToArray());
			}
		}
	}

	/// <summary>Occurs when an individual source's loading state changes.</summary>
	public event EventHandler? SourceStatesChanged;

	/// <inheritdoc />
	public event EventHandler<FolderChangeEventArgs>? Changed;

	// Source failures are exposed in Sources rather than as fatal location watcher faults.
	event EventHandler<FolderChangeErrorEventArgs>? IFolderChangeSource.Faulted { add { } remove { } }

	/// <summary>Initializes an aggregate context over the current visible sources.</summary>
	/// <param name="location">The home location.</param>
	/// <param name="workspace">The borrowed storage workspace.</param>
	public HomeBrowseLocationContext(HomeLocation location, IStorageWorkspace workspace)
	{
		ArgumentNullException.ThrowIfNull(location);

		ArgumentNullException.ThrowIfNull(workspace);

		Location = location;
		_workspace = workspace;
		_sources = workspace.Sources.Select(source => new SourceEntry(source)).ToArray();
		if (workspace.SourceRegistry is { } registry)
		{
			registry.SourcesChanged += OnSourcesChanged;
		}
	}

	/// <inheritdoc />
	public ValueTask StartAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (!_sources.Select(entry => entry.Source).SequenceEqual(_workspace.Sources))
		{
			OnSourcesChanged(this, EventArgs.Empty);
		}

		return ValueTask.CompletedTask;
	}

	/// <inheritdoc />
	public async IAsyncEnumerable<IStorableModel> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
		var channel = Channel.CreateBounded<IStorableModel>(16);
		Task[] producers;
		lock (_syncRoot)
		{
			ObjectDisposedException.ThrowIf(_isDisposed, this);

			if (_enumerationStarted)
			{
				throw new InvalidOperationException("A Home context can enumerate its initial roots only once.");
			}

			_enumerationStarted = true;
			producers = _sources.Select(entry => entry.InitialEnumeration = Task.Run(() => ProduceRootsAsync(entry, channel.Writer, linked.Token))).ToArray();
			_work.AddRange(producers);
		}

		var completion = CompleteChannelAsync(channel.Writer, producers);
		try
		{
			await foreach (var item in channel.Reader.ReadAllAsync(linked.Token).ConfigureAwait(false))
			{
				yield return item;
			}
		}
		finally
		{
			linked.Cancel();
			await completion.ConfigureAwait(false);
			while (channel.Reader.TryRead(out var abandoned))
			{
				await abandoned.DisposeAsync().ConfigureAwait(false);
			}
		}
	}

	/// <summary>Retries only one source, retaining its previous roots if enumeration fails.</summary>
	/// <param name="sourceId">The failed or stale source identity.</param>
	/// <param name="cancellationToken">Cancels this retry; source errors are recorded in Sources.</param>
	/// <returns>The retry operation. Successful changes are published through the normal browse change pipeline.</returns>
	public ValueTask RetrySourceAsync(StorageSourceId sourceId, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(sourceId);

		lock (_syncRoot)
		{
			ObjectDisposedException.ThrowIf(_isDisposed, this);

			if (!_enumerationStarted)
			{
				throw new InvalidOperationException("Initial Home enumeration has not started.");
			}

			var entry = _sources.FirstOrDefault(entry => entry.Source.SourceId == sourceId) ?? throw new KeyNotFoundException("The Home source was not found.");
			_work.RemoveAll(static task => task.IsCompleted);
			var task = Task.Run(() => RetrySourceCoreAsync(entry, cancellationToken));
			_work.Add(task);

			return new ValueTask(task);
		}
	}

	/// <inheritdoc />
	public ValueTask<IStorableModel> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		lock (_syncRoot)
		{
			ObjectDisposedException.ThrowIf(_isDisposed, this);
		}

		return _workspace.ResolveAsync(reference, cancellationToken);
	}

	/// <inheritdoc />
	public void Dispose()
	{
		_ = DisposeAsync();
	}

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		lock (_syncRoot)
		{
			if (_disposeTask is not null)
			{
				return new ValueTask(_disposeTask);
			}

			_isDisposed = true;
			_disposeTask = DisposeCoreAsync();

			return new ValueTask(_disposeTask);
		}
	}

	private async Task ProduceRootsAsync(SourceEntry entry, ChannelWriter<IStorableModel> writer, CancellationToken cancellationToken)
	{
		await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			SetState(entry, HomeSourceLoadState.Loading);
			await foreach (var root in _workspace.GetRootsAsync(entry.Source.SourceId, cancellationToken).ConfigureAwait(false))
			{
				var handedOff = false;
				try
				{
					await writer.WriteAsync(root, cancellationToken).ConfigureAwait(false);
					handedOff = true;
					lock (_syncRoot)
					{
						entry.Roots[root.Reference.ItemId] = root.Reference;
					}
				}
				finally
				{
					if (!handedOff)
					{
						await root.DisposeAsync().ConfigureAwait(false);
					}
				}
			}

			SetState(entry, HomeSourceLoadState.Ready);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception error)
		{
			SetState(entry, HomeSourceLoadState.Failed, error);
		}
		finally
		{
			entry.Gate.Release();
		}
	}

	private async Task RetrySourceCoreAsync(SourceEntry entry, CancellationToken cancellationToken)
	{
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
		await entry.InitialEnumeration.WaitAsync(linked.Token).ConfigureAwait(false);
		await entry.Gate.WaitAsync(linked.Token).ConfigureAwait(false);
		try
		{
			SetState(entry, HomeSourceLoadState.Loading);
			var next = new Dictionary<string, StorableReference>(StringComparer.Ordinal);
			await foreach (var root in _workspace.GetRootsAsync(entry.Source.SourceId, linked.Token).ConfigureAwait(false))
			{
				await using (root.ConfigureAwait(false))
				{
					next[root.Reference.ItemId] = root.Reference;
				}
			}

			linked.Token.ThrowIfCancellationRequested();

			StorableReference[] previous;
			lock (_syncRoot)
			{
				previous = entry.Roots.Values.ToArray();
				entry.Roots = next;
			}

			foreach (var removed in previous.Where(reference => !next.ContainsKey(reference.ItemId)))
			{
				Changed?.Invoke(this, new FolderChangeEventArgs(new FolderChange(FolderChangeKind.Deleted, null, removed, false)));
			}

			foreach (var reference in next.Values)
			{
				var kind = previous.Any(item => item.ItemId == reference.ItemId) ? FolderChangeKind.Updated : FolderChangeKind.Created;
				Changed?.Invoke(this, new FolderChangeEventArgs(new FolderChange(kind, reference, null, false)));
			}

			SetState(entry, HomeSourceLoadState.Ready);
		}
		catch (OperationCanceledException) when (linked.IsCancellationRequested)
		{
			SetState(entry, HomeSourceLoadState.NotStarted);
			throw;
		}
		catch (Exception error)
		{
			SetState(entry, HomeSourceLoadState.Failed, error);
		}
		finally
		{
			entry.Gate.Release();
		}
	}

	private void SetState(SourceEntry entry, HomeSourceLoadState state, Exception? error = null)
	{
		lock (_syncRoot)
		{
			entry.State = new HomeSourceState(entry.Source.SourceId, entry.Source.DisplayName, state, entry.Roots.Count, error);
		}

		SourceStatesChanged?.Invoke(this, EventArgs.Empty);
	}

	private void OnSourcesChanged(object? sender, EventArgs args)
	{
		Changed?.Invoke(this, new FolderChangeEventArgs(new FolderChange(FolderChangeKind.DirectoryUpdated, null, null, true)));
	}

	private async Task DisposeCoreAsync()
	{
		if (_workspace.SourceRegistry is { } registry)
		{
			registry.SourcesChanged -= OnSourcesChanged;
		}

		_lifetime.Cancel();
		try
		{
			await Task.WhenAll(_work.ToArray()).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			foreach (var entry in _sources)
			{
				entry.Gate.Dispose();
			}

			_lifetime.Dispose();
		}
	}

	private static async Task CompleteChannelAsync(ChannelWriter<IStorableModel> writer, Task[] producers)
	{
		try
		{
			await Task.WhenAll(producers).ConfigureAwait(false);
			writer.TryComplete();
		}
		catch (Exception error)
		{
			writer.TryComplete(error);
		}
	}

	private sealed class SourceEntry
	{
		public IStorageSource Source { get; }
		public SemaphoreSlim Gate { get; } = new(1, 1);
		public Task InitialEnumeration { get; set; } = Task.CompletedTask;
		public Dictionary<string, StorableReference> Roots { get; set; } = new(StringComparer.Ordinal);
		public HomeSourceState State { get; set; }

		public SourceEntry(IStorageSource source)
		{
			Source = source;
			State = new HomeSourceState(source.SourceId, source.DisplayName, HomeSourceLoadState.NotStarted, 0, null);
		}
	}
}

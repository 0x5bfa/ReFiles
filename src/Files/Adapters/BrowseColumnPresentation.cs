// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Sessions;
using Files.Core.ViewSettings;
using Files.ViewModels;

namespace Files.Adapters;

internal sealed class BrowseColumnPresentation : IAsyncDisposable
{
	private readonly BrowsePaneSession _pane;
	private readonly Action<long, IReadOnlyList<DetailsColumnViewModel>> _publish;
	private readonly Lock _pendingLock = new();
	private readonly CancellationTokenSource _lifetime = new();
	private ColumnLoad? _columnLoad;
	private ColumnCache? _columnCache;
	private int _isDisposed;

	internal BrowseColumnPresentation(BrowsePaneSession pane, Action<long, IReadOnlyList<DetailsColumnViewModel>> publish)
	{
		_pane = pane;
		_publish = publish;
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
		{
			return;
		}

		ColumnLoad? load;
		lock (_pendingLock)
		{
			load = _columnLoad;
			_columnCache = null;
		}

		load?.Cancel();
		_lifetime.Cancel();
		try
		{
			if (load is not null)
			{
				await load.Task.ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			_lifetime.Dispose();
		}
	}

	internal void Update()
	{
		if (Volatile.Read(ref _isDisposed) is not 0)
		{
			return;
		}

		var session = _pane.BrowseSession;
		if (session.Context is not IBrowseColumnProvider context || session.Generation is 0)
		{
			CancelColumnsLoad();
			QueueColumns(session.Generation, CreateFallbackColumns());

			return;
		}

		var hasCachedColumns = false;
		BrowseColumnSet? cachedColumnSet = null;
		ColumnLoad? previousLoad = null;
		ColumnLoad? loadToStart = null;
		Task previousTask = Task.CompletedTask;
		lock (_pendingLock)
		{
			if (_columnCache is { } cache && cache.Generation == session.Generation && ReferenceEquals(cache.Context, context))
			{
				hasCachedColumns = true;
				cachedColumnSet = cache.ColumnSet;
			}
			else if (_columnLoad is { } activeLoad && activeLoad.Generation == session.Generation && ReferenceEquals(activeLoad.Context, context))
			{
				return;
			}
			else
			{
				previousLoad = _columnLoad;
				previousTask = previousLoad?.Task ?? Task.CompletedTask;
				loadToStart = new ColumnLoad(context, session.Generation, _lifetime.Token);
				_columnLoad = loadToStart;
				_columnCache = null;
			}
		}

		previousLoad?.Cancel();
		if (loadToStart is { } load)
		{
			load.Start(() => LoadColumnsAsync(load, previousTask));
		}

		if (hasCachedColumns)
		{
			QueueColumns(session.Generation, cachedColumnSet is null ? CreateFallbackColumns() : CreateDetailsColumns(cachedColumnSet, session.ViewSettings));
		}
	}

	private async Task LoadColumnsAsync(ColumnLoad load, Task previousTask)
	{
		try
		{
			await previousTask.ConfigureAwait(false);
			load.Token.ThrowIfCancellationRequested();

			var columnSet = await load.Context.GetColumnsAsync(load.Token).ConfigureAwait(false);
			if (!IsCurrentColumnsContext(load.Context, load.Generation, load.Token))
			{
				return;
			}

			lock (_pendingLock)
			{
				if (IsCurrentColumnsContext(load.Context, load.Generation, load.Token))
				{
					_columnCache = new ColumnCache(load.Context, load.Generation, columnSet);
				}
			}

			if (columnSet is null)
			{
				QueueColumns(load.Generation, CreateFallbackColumns());

				return;
			}

			var session = _pane.BrowseSession;
			var capabilities = BrowseViewCapabilities.FromColumns(columnSet);
			if (load.Context is IViewSettingsCapabilityProvider capabilityProvider)
			{
				capabilities = await capabilityProvider.GetViewSettingsCapabilitiesAsync(load.Token).ConfigureAwait(false) ?? capabilities;
			}

			if (session is IViewSettingsSession viewSettingsSession)
			{
				await viewSettingsSession.ApplyViewCapabilitiesAsync(capabilities, load.Token).ConfigureAwait(false);
				if (!IsCurrentColumnsContext(load.Context, load.Generation, load.Token))
				{
					return;
				}
			}

			QueueColumns(load.Generation, CreateDetailsColumns(columnSet, session.ViewSettings));
		}
		catch (OperationCanceledException) when (load.IsCancellationRequested)
		{
		}
		catch
		{
			if (IsCurrentColumnsContext(load.Context, load.Generation, CancellationToken.None))
			{
				QueueColumns(load.Generation, CreateFallbackColumns());
			}
		}
		finally
		{
			lock (_pendingLock)
			{
				if (ReferenceEquals(_columnLoad, load))
				{
					_columnLoad = null;
				}
			}

			load.Dispose();
		}
	}

	private void CancelColumnsLoad()
	{
		ColumnLoad? load;
		lock (_pendingLock)
		{
			load = _columnLoad;
			_columnCache = null;
		}

		load?.Cancel();
	}

	private bool IsCurrentColumnsContext(IBrowseColumnProvider context, long generation, CancellationToken cancellationToken)
	{
		return !cancellationToken.IsCancellationRequested &&
			_pane.BrowseSession.Generation == generation &&
			ReferenceEquals(_pane.BrowseSession.Context, context) &&
			Volatile.Read(ref _isDisposed) is 0;
	}

	private void QueueColumns(long generation, IReadOnlyList<DetailsColumnViewModel> columns)
	{
		if (Volatile.Read(ref _isDisposed) == 0)
		{
			_publish(generation, columns);
		}
	}

	private static IReadOnlyList<DetailsColumnViewModel> CreateDetailsColumns(BrowseColumnSet columnSet, BrowseViewSettings settings)
	{
		var availableColumns = columnSet.All
			.Where(static column => !column.IsHidden)
			.GroupBy(static column => column.PropertyId, StringComparer.Ordinal)
			.ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
		var selectedColumns = new List<BrowseColumn>();
		var selectedPropertyIds = new HashSet<string>(StringComparer.Ordinal);

		foreach (var configuredColumn in settings.Columns.Where(static column => column.IsVisible).OrderBy(static column => column.Order))
		{
			if (availableColumns.TryGetValue(configuredColumn.PropertyId, out var column) && selectedPropertyIds.Add(column.PropertyId))
			{
				selectedColumns.Add(column);
			}
		}

		if (selectedColumns.Count is 0)
		{
			foreach (var column in columnSet.DefaultVisible)
			{
				if (availableColumns.ContainsKey(column.PropertyId) && selectedPropertyIds.Add(column.PropertyId))
				{
					selectedColumns.Add(column);
				}
			}
		}

		if (selectedColumns.Count is 0)
		{
			foreach (var column in availableColumns.Values.OrderBy(static column => column.Index))
			{
				if (selectedPropertyIds.Add(column.PropertyId))
				{
					selectedColumns.Add(column);
				}
			}
		}

		if (selectedColumns.Count is 0)
		{
			return CreateFallbackColumns();
		}

		var result = new List<DetailsColumnViewModel>(selectedColumns.Count);
		foreach (var column in selectedColumns)
		{
			var configuredColumn = settings.Columns.FirstOrDefault(setting => setting.PropertyId.Equals(column.PropertyId, StringComparison.Ordinal));
			var width = configuredColumn?.Width ?? GetDefaultColumnWidth(column);
			var isNameColumn = column.PropertyId.Equals("System.ItemNameDisplay", StringComparison.Ordinal) || column.PropertyId.Equals("name", StringComparison.OrdinalIgnoreCase);
			result.Add(new DetailsColumnViewModel(column.PropertyId, column.DisplayName, width, column.Alignment, isNameColumn, !column.IsFixedWidth, canSort: column.CanSort, canGroup: column.CanGroup));
		}

		return Array.AsReadOnly(result.ToArray());
	}

	private static double GetDefaultColumnWidth(BrowseColumn column)
	{
		return column.DefaultWidth;
	}

	internal static IReadOnlyList<DetailsColumnViewModel> CreateFallbackColumns()
	{
		return Array.AsReadOnly(new[]
		{
			new DetailsColumnViewModel("System.ItemNameDisplay", "System.ItemNameDisplay", 180, BrowseColumnAlignment.Left, isPrimary: true),
			new DetailsColumnViewModel("System.ItemTypeText", "System.ItemTypeText", 100, BrowseColumnAlignment.Left),
			new DetailsColumnViewModel("reference", "reference", 220, BrowseColumnAlignment.Left),
		});
	}

	private sealed record ColumnCache(IBrowseColumnProvider Context, long Generation, BrowseColumnSet? ColumnSet);

	private sealed class ColumnLoad : IDisposable
	{
		private readonly CancellationTokenSource _cancellation;
		private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public IBrowseColumnProvider Context { get; }

		public long Generation { get; }

		public CancellationToken Token => _cancellation.Token;

		public bool IsCancellationRequested => _cancellation.IsCancellationRequested;

		public Task Task => _completion.Task;

		public ColumnLoad(IBrowseColumnProvider context, long generation, CancellationToken lifetimeToken)
		{
			Context = context;
			Generation = generation;
			_cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
		}

		public void Start(Func<Task> action)
		{
			ArgumentNullException.ThrowIfNull(action);

			_ = RunAsync(action);
		}

		public void Cancel()
		{
			try
			{
				_cancellation.Cancel();
			}
			catch (ObjectDisposedException)
			{
			}
		}

		public void Dispose()
		{
			_cancellation.Dispose();
		}

		private async Task RunAsync(Func<Task> action)
		{
			try
			{
				await action().ConfigureAwait(false);
				_completion.TrySetResult(true);
			}
			catch (OperationCanceledException exception)
			{
				_completion.TrySetCanceled(exception.CancellationToken);
			}
			catch (Exception exception)
			{
				_completion.TrySetException(exception);
			}
		}
	}
}

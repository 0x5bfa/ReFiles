// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Models;

namespace Files.Core.AppModels;

public enum PaneNavigationMode
{
	Push,
	Replace,
}

/// <summary>
/// Owns browsing, history, preview, and viewport work for one pane.
/// </summary>
public sealed class PaneModel : IAsyncDisposable
{
	private readonly SemaphoreSlim _navigationLock = new(1, 1);

	private readonly CancellationTokenSource _lifetime = new();

	private readonly Lock _disposalLock = new();
	private readonly Lock _navigationCancellationLock = new();

	private Task? _disposeTask;
	private CancellationTokenSource? _activeNavigationCancellation;

	private volatile bool _isDisposed;

	public Guid Id { get; }

	public IBrowseSessionModel BrowseSession { get; }

	public IBrowsePreviewModel Preview { get; }

	public IBrowsePrefetchCoordinator Prefetch { get; }

	public BrowseNavigationHistory History { get; }

	public BrowseLocation? Location => BrowseSession.Location;

	public bool CanGoBack => History.CanGoBack;

	public bool CanGoForward => History.CanGoForward;

	public bool CanGoUp =>
		BrowseSession.Context is IBrowseLocationParentResolver parentResolver
			? parentResolver.CanGetParent
			: BrowseSession.Context?.LocationModel
				is IFolderModel;

	public event EventHandler? StateChanged;

	public PaneModel(IBrowseSessionModel browseSession, IBrowsePreviewModel preview, IBrowsePrefetchCoordinator prefetch, int historyCapacity = 50, Guid? id = null)
	{
		ArgumentNullException.ThrowIfNull(browseSession);
		ArgumentNullException.ThrowIfNull(preview);
		ArgumentNullException.ThrowIfNull(prefetch);

		Id = id ?? Guid.NewGuid();
		if (Id == Guid.Empty)
		{
			throw new ArgumentException("A pane ID cannot be empty.", nameof(id));
		}

		BrowseSession = browseSession;
		Preview = preview;
		Prefetch = prefetch;
		History = new BrowseNavigationHistory(historyCapacity);

		BrowseSession.StateChanged += OnChildStateChanged;
		Preview.Changed += OnChildStateChanged;
		History.Changed += OnChildStateChanged;
	}

	public async ValueTask NavigateAsync(BrowseLocation location, PaneNavigationMode mode = PaneNavigationMode.Push, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(location);

		if (mode is not PaneNavigationMode.Push and not PaneNavigationMode.Replace)
		{
			throw new ArgumentOutOfRangeException(nameof(mode));
		}

		using var navigation = BeginNavigation(cancellationToken);
		await _navigationLock.WaitAsync(navigation.Token).ConfigureAwait(false);

		try
		{
			EnsureActive();
			await NavigateAndCommitAsync(location, () => { if (mode is PaneNavigationMode.Push) { History.Push(location); } else { History.Replace(location); } }, navigation.Token).ConfigureAwait(false);
		}
		finally
		{
			_navigationLock.Release();
		}
	}

	public async ValueTask<bool> GoBackAsync(CancellationToken cancellationToken = default)
	{
		using var navigation = BeginNavigation(cancellationToken);
		await _navigationLock.WaitAsync(navigation.Token).ConfigureAwait(false);

		try
		{
			EnsureActive();
			if (!History.TryGetBack(out var target, out var targetIndex) || target is null)
			{
				return false;
			}

			await NavigateAndCommitAsync(target, () => History.TryMoveTo(targetIndex, target), navigation.Token).ConfigureAwait(false);

			return Equals(BrowseSession.Location, target);
		}
		finally
		{
			_navigationLock.Release();
		}
	}

	public async ValueTask<bool> GoForwardAsync(CancellationToken cancellationToken = default)
	{
		using var navigation = BeginNavigation(cancellationToken);
		await _navigationLock.WaitAsync(navigation.Token).ConfigureAwait(false);

		try
		{
			EnsureActive();
			if (!History.TryGetForward(out var target, out var targetIndex) || target is null)
			{
				return false;
			}

			await NavigateAndCommitAsync(target, () => History.TryMoveTo(targetIndex, target), navigation.Token).ConfigureAwait(false);

			return Equals(BrowseSession.Location, target);
		}
		finally
		{
			_navigationLock.Release();
		}
	}

	public async ValueTask<bool> GoUpAsync(CancellationToken cancellationToken = default)
	{
		using var navigation = BeginNavigation(cancellationToken);
		await _navigationLock.WaitAsync(navigation.Token).ConfigureAwait(false);

		try
		{
			EnsureActive();
			if (BrowseSession.Context is IBrowseLocationParentResolver parentResolver)
			{
				var parentLocation = await parentResolver.GetParentLocationAsync(navigation.Token).ConfigureAwait(false);
				if (parentLocation is null)
				{
					return false;
				}

				await NavigateAndCommitAsync(parentLocation, () => History.Push(parentLocation), navigation.Token).ConfigureAwait(false);

				return Equals(BrowseSession.Location, parentLocation);
			}

			if (BrowseSession.Context?.LocationModel is not IFolderModel folder)
			{
				return false;
			}

			var parent = await folder.GetParentAsync(navigation.Token).ConfigureAwait(false);
			if (parent is null)
			{
				return false;
			}

			await using (parent.ConfigureAwait(false))
			{
				var target = new FolderLocation(parent.Reference);
				await NavigateAndCommitAsync(target, () => History.Push(target), navigation.Token).ConfigureAwait(false);

				return Equals(BrowseSession.Location, target);
			}
		}
		finally
		{
			_navigationLock.Release();
		}
	}

	public async ValueTask RestoreAsync(BrowseNavigationHistorySnapshot restoredHistory, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(restoredHistory);

		using var navigation = BeginNavigation(cancellationToken);
		await _navigationLock.WaitAsync(navigation.Token).ConfigureAwait(false);

		try
		{
			EnsureActive();
			if (restoredHistory.Current is not { } target)
			{
				if (BrowseSession.Location is not null)
				{
					throw new InvalidOperationException("An active pane cannot be restored to an empty history.");
				}

				History.Restore(restoredHistory);

				return;
			}

			await NavigateAndCommitAsync(target, () => History.Restore(restoredHistory), navigation.Token).ConfigureAwait(false);
		}
		finally
		{
			_navigationLock.Release();
		}
	}

	public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
	{
		using var navigation = BeginNavigation(cancellationToken);
		await _navigationLock.WaitAsync(navigation.Token).ConfigureAwait(false);

		try
		{
			EnsureActive();
			await BrowseSession.RefreshAsync(navigation.Token).ConfigureAwait(false);
		}
		finally
		{
			_navigationLock.Release();
		}
	}

	public void UpdateViewport(BrowseViewport viewport)
	{
		EnsureActive();

		ArgumentNullException.ThrowIfNull(viewport);

		Prefetch.UpdateViewport(viewport, BrowseSession.ViewSettings, BrowseSession.Generation);
	}

	public ValueTask DisposeAsync()
	{
		lock (_disposalLock)
		{
			if (_disposeTask is not null)
			{
				return new ValueTask(_disposeTask);
			}

			_isDisposed = true;
			_lifetime.Cancel();
			_disposeTask = DisposeCoreAsync();

			return new ValueTask(_disposeTask);
		}
	}

	private async Task NavigateAndCommitAsync(BrowseLocation target, Action commitHistory, CancellationToken cancellationToken)
	{
		var previousGeneration = BrowseSession.Generation;
		var completed = false;

		try
		{
			await BrowseSession.NavigateAsync(target, cancellationToken).ConfigureAwait(false);
			completed = true;
		}
		finally
		{
			if ((completed || (!cancellationToken.IsCancellationRequested && BrowseSession.Generation != previousGeneration)) && Equals(BrowseSession.Location, target))
			{
				commitHistory();
			}
		}
	}

	private async Task DisposeCoreAsync()
	{
		var errors = new List<Exception>();

		BrowseSession.StateChanged -= OnChildStateChanged;
		Preview.Changed -= OnChildStateChanged;
		History.Changed -= OnChildStateChanged;

		await _navigationLock.WaitAsync().ConfigureAwait(false);

		try
		{
			await TryDisposeAsync(Prefetch, errors).ConfigureAwait(false);
			await TryDisposeAsync(Preview, errors).ConfigureAwait(false);
			await TryDisposeAsync(BrowseSession, errors).ConfigureAwait(false);
		}
		finally
		{
			_navigationLock.Release();
			_navigationLock.Dispose();
			_lifetime.Dispose();
			GC.SuppressFinalize(this);
		}

		if (errors.Count is 1)
		{
			throw errors[0];
		}

		if (errors.Count > 1)
		{
			throw new AggregateException("One or more pane resources could not be disposed.", errors);
		}
	}

	private static async ValueTask TryDisposeAsync(IAsyncDisposable disposable, ICollection<Exception> errors)
	{
		try
		{
			await disposable.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception error)
		{
			errors.Add(error);
		}
	}

	private NavigationOperation BeginNavigation(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
		CancellationTokenSource? previousCancellation;
		lock (_navigationCancellationLock)
		{
			previousCancellation = _activeNavigationCancellation;
			_activeNavigationCancellation = operationCancellation;
		}

		try
		{
			previousCancellation?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}

		return new NavigationOperation(this, operationCancellation);
	}

	private void EndNavigation(CancellationTokenSource operationCancellation)
	{
		lock (_navigationCancellationLock)
		{
			if (ReferenceEquals(_activeNavigationCancellation, operationCancellation))
			{
				_activeNavigationCancellation = null;
			}
		}

		operationCancellation.Dispose();
	}

	private void EnsureActive()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

	}

	private void OnChildStateChanged(object? sender, EventArgs args)
	{
		ModelEvent.Raise(this, StateChanged);
	}

	private sealed class NavigationOperation : IDisposable
	{
		private readonly PaneModel _owner;
		private readonly CancellationTokenSource _cancellation;
		private int _isDisposed;

		public CancellationToken Token => _cancellation.Token;

		public NavigationOperation(PaneModel owner, CancellationTokenSource cancellation)
		{
			_owner = owner;
			_cancellation = cancellation;
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
			{
				return;
			}

			_owner.EndNavigation(_cancellation);
		}
	}
}

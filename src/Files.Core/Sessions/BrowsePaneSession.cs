// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Models;

namespace Files.Core.Sessions;

/// <summary>Specifies how navigation updates pane history.</summary>
public enum PaneNavigationMode
{
	/// <summary>Adds the target after the current history entry.</summary>
	Push,

	/// <summary>Replaces the current history entry.</summary>
	Replace,
}

/// <summary>
/// Owns browsing, history, and preview state for browse pane content.
/// </summary>
public sealed class BrowsePaneSession : IPaneContentSession
{
	private readonly SemaphoreSlim _navigationLock = new(1, 1);

	private readonly CancellationTokenSource _lifetime = new();

	private readonly Lock _disposalLock = new();
	private readonly Lock _navigationCancellationLock = new();

	private Task? _disposeTask;
	private CancellationTokenSource? _activeNavigationCancellation;

	private volatile bool _isDisposed;

	/// <summary>Gets the browse state owned by this content session.</summary>
	public IBrowseSession BrowseSession { get; }

	/// <summary>Gets the preview state owned by this content session.</summary>
	public IBrowsePreviewModel Preview { get; }

	/// <summary>Gets the navigation history.</summary>
	public BrowseNavigationHistory History { get; }

	/// <summary>Gets the current browse location.</summary>
	public BrowseLocation? Location => BrowseSession.Location;

	/// <summary>Gets a value indicating whether backward navigation is available.</summary>
	public bool CanGoBack => History.CanGoBack;

	/// <summary>Gets a value indicating whether forward navigation is available.</summary>
	public bool CanGoForward => History.CanGoForward;

	/// <summary>Gets a value indicating whether parent navigation is available.</summary>
	public bool CanGoUp =>
		BrowseSession.Context is IBrowseLocationParentResolver parentResolver
			? parentResolver.CanGetParent
			: BrowseSession.Context?.LocationModel
				is IFolderModel;

	/// <summary>Occurs when navigation, history, or preview state changes.</summary>
	public event EventHandler? NavigationStateChanged;

	/// <summary>Initializes browse pane content and takes ownership of its collaborators.</summary>
	/// <param name="browseSession">The browse session to own.</param>
	/// <param name="preview">The preview model to own.</param>
	/// <param name="historyCapacity">The maximum navigation history length.</param>
	public BrowsePaneSession(IBrowseSession browseSession, IBrowsePreviewModel preview, int historyCapacity = 50)
	{
		ArgumentNullException.ThrowIfNull(browseSession);
		ArgumentNullException.ThrowIfNull(preview);

		BrowseSession = browseSession;
		Preview = preview;
		History = new BrowseNavigationHistory(historyCapacity);

		BrowseSession.StateChanged += OnChildStateChanged;
		Preview.Changed += OnChildStateChanged;
		History.Changed += OnChildStateChanged;
	}

	/// <summary>Navigates to a location and commits it to history.</summary>
	/// <param name="location">The target location.</param>
	/// <param name="mode">The history update mode.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <param name="ownerWindowHandle">The native owner for UI shown while enumerating, or zero to suppress such UI.</param>
	/// <returns>A task that represents the navigation.</returns>
	public async ValueTask NavigateAsync(BrowseLocation location, PaneNavigationMode mode = PaneNavigationMode.Push, CancellationToken cancellationToken = default, nint ownerWindowHandle = 0)
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
			await NavigateAndCommitAsync(location, () =>
			{
				if (mode is PaneNavigationMode.Push) { History.Push(location); } else { History.Replace(location); }
			}, ownerWindowHandle, navigation.Token).ConfigureAwait(false);
		}
		finally
		{
			_navigationLock.Release();
		}
	}

	/// <summary>Navigates to the previous history entry.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <param name="ownerWindowHandle">The native owner for UI shown while enumerating, or zero to suppress such UI.</param>
	/// <returns><see langword="true"/> when navigation completed.</returns>
	public async ValueTask<bool> GoBackAsync(CancellationToken cancellationToken = default, nint ownerWindowHandle = 0)
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

			await NavigateAndCommitAsync(target, () => History.TryMoveTo(targetIndex, target), ownerWindowHandle, navigation.Token).ConfigureAwait(false);

			return Equals(BrowseSession.Location, target);
		}
		finally
		{
			_navigationLock.Release();
		}
	}

	/// <summary>Navigates to the following history entry.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <param name="ownerWindowHandle">The native owner for UI shown while enumerating, or zero to suppress such UI.</param>
	/// <returns><see langword="true"/> when navigation completed.</returns>
	public async ValueTask<bool> GoForwardAsync(CancellationToken cancellationToken = default, nint ownerWindowHandle = 0)
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

			await NavigateAndCommitAsync(target, () => History.TryMoveTo(targetIndex, target), ownerWindowHandle, navigation.Token).ConfigureAwait(false);

			return Equals(BrowseSession.Location, target);
		}
		finally
		{
			_navigationLock.Release();
		}
	}

	/// <summary>Navigates to the current location's parent.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <param name="ownerWindowHandle">The native owner for UI shown while enumerating, or zero to suppress such UI.</param>
	/// <returns><see langword="true"/> when a parent was available and navigation completed.</returns>
	public async ValueTask<bool> GoUpAsync(CancellationToken cancellationToken = default, nint ownerWindowHandle = 0)
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

				await NavigateAndCommitAsync(parentLocation, () => History.Push(parentLocation), ownerWindowHandle, navigation.Token).ConfigureAwait(false);

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
				await NavigateAndCommitAsync(target, () => History.Push(target), ownerWindowHandle, navigation.Token).ConfigureAwait(false);

				return Equals(BrowseSession.Location, target);
			}
		}
		finally
		{
			_navigationLock.Release();
		}
	}

	/// <summary>Restores a navigation history snapshot and its current location.</summary>
	/// <param name="restoredHistory">The snapshot to restore.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A task that represents the restore operation.</returns>
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

			await NavigateAndCommitAsync(target, () => History.Restore(restoredHistory), 0, navigation.Token).ConfigureAwait(false);
		}
		finally
		{
			_navigationLock.Release();
		}
	}

	/// <summary>Refreshes the current location.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <param name="ownerWindowHandle">The native owner for UI shown while enumerating, or zero to suppress such UI.</param>
	/// <returns>A task that represents the refresh.</returns>
	public async ValueTask RefreshAsync(CancellationToken cancellationToken = default, nint ownerWindowHandle = 0)
	{
		using var navigation = BeginNavigation(cancellationToken);
		await _navigationLock.WaitAsync(navigation.Token).ConfigureAwait(false);

		try
		{
			EnsureActive();
			if (ownerWindowHandle is not 0 && BrowseSession is IInteractiveBrowseSession interactiveSession)
			{
				await interactiveSession.RefreshAsync(ownerWindowHandle, navigation.Token).ConfigureAwait(false);
			}
			else
			{
				await BrowseSession.RefreshAsync(navigation.Token).ConfigureAwait(false);
			}
		}
		finally
		{
			_navigationLock.Release();
		}
	}

	/// <inheritdoc />
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

	private async Task NavigateAndCommitAsync(BrowseLocation target, Action commitHistory, nint ownerWindowHandle, CancellationToken cancellationToken)
	{
		var previousGeneration = BrowseSession.Generation;
		var completed = false;

		try
		{
			if (ownerWindowHandle is not 0 && BrowseSession is IInteractiveBrowseSession interactiveSession)
			{
				await interactiveSession.NavigateAsync(target, ownerWindowHandle, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				await BrowseSession.NavigateAsync(target, cancellationToken).ConfigureAwait(false);
			}
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
		SessionEvent.Raise(this, NavigationStateChanged);
	}

	private sealed class NavigationOperation : IDisposable
	{
		private readonly BrowsePaneSession _owner;
		private readonly CancellationTokenSource _cancellation;
		private int _isDisposed;

		public CancellationToken Token => _cancellation.Token;

		public NavigationOperation(BrowsePaneSession owner, CancellationTokenSource cancellation)
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

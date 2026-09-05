// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Previews;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.ViewSettings;
using Files.Core.Windows;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for browse preview model behavior.
/// </summary>
[TestClass]
public sealed class BrowsePreviewModelTests
{
	/// <summary>
	/// Test case: selection change disposes stale result and publishes latest selection.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task SelectionChangeDisposesStaleResultAndPublishesLatestSelection()
	{
		var factory = new TestModelFactory();
		var folder = factory.CreateModel("folder", "Folder", out _);
		var firstSource = new TestPreviewSource();
		var secondResult = new TestPreviewResult();
		var firstResult = new TestPreviewResult();
		var firstRelease = new TaskCompletionSource<PreviewResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
		firstSource.Handler = async _ => await firstRelease.Task;
		var secondSource = new TestPreviewSource { Result = secondResult };
		var first = factory.CreateModel("first", "First", out _, previewSource: firstSource);
		var second = factory.CreateModel("second", "Second", out _, previewSource: secondSource);
		var resolver = new TestBrowseLocationResolver([first, second])
		{
			LocationModelFactory = _ => folder,
		};
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(folder.Reference));
		await using var preview = new BrowsePreviewModel(session, TimeSpan.Zero);

		session.SetSelection([first.Reference.GetKey()], first.Reference.GetKey(), null);
		await firstSource.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

		session.SetSelection([second.Reference.GetKey()], second.Reference.GetKey(), null);
		await WaitUntilAsync(() => preview.Current.Status is BrowsePreviewStatus.Ready && preview.Current.TargetKey == second.Reference.GetKey());

		firstRelease.TrySetResult(firstResult);
		await firstSource.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await WaitUntilAsync(() => firstResult.IsDisposed);

		Assert.IsTrue(firstResult.IsDisposed);
		Assert.AreSame(secondResult, preview.Current.Result);
		Assert.AreEqual(BrowsePreviewStatus.Ready, preview.Current.Status);
	}

	/// <summary>
	/// Test case: rejects result from replaced model with the same key.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task RejectsResultFromReplacedModelWithTheSameKey()
	{
		var factory = new TestModelFactory();
		var firstFolder = factory.CreateModel("folder-1", "Folder", out _);
		var secondFolder = factory.CreateModel("folder-2", "Folder", out _);
		var oldSource = new TestPreviewSource();
		var oldResult = new TestPreviewResult();
		var oldRelease = new TaskCompletionSource<PreviewResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
		oldSource.Handler = async _ => await oldRelease.Task;
		var replacementResult = new TestPreviewResult();
		var replacementSource = new TestPreviewSource { Result = replacementResult };
		var previous = factory.CreateModel("item", "Before", out _, previewSource: oldSource);
		var replacement = factory.CreateModel("item", "After", out _, previewSource: replacementSource);
		var resolverLocations = new Queue<IStorableModel>([firstFolder, secondFolder]);
		var resolver = new TestBrowseLocationResolver([previous])
		{
			LocationModelFactory = _ =>
				resolverLocations.Dequeue(),
		};
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(firstFolder.Reference));
		await using var preview = new BrowsePreviewModel(session, TimeSpan.Zero);

		session.SetSelection([previous.Reference.GetKey()], previous.Reference.GetKey(), null);
		await oldSource.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

		resolver.Items.Clear();
		resolver.Items.Add(replacement);
		await session.RefreshAsync();
		await WaitUntilAsync(() => preview.Current.Status is BrowsePreviewStatus.Ready && ReferenceEquals(preview.Current.Result, replacementResult));

		oldRelease.TrySetResult(oldResult);
		await oldSource.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await WaitUntilAsync(() => oldResult.IsDisposed);

		Assert.IsTrue(oldResult.IsDisposed);
		Assert.AreEqual(previous.Reference.GetKey(), replacement.Reference.GetKey());
		Assert.AreSame(replacementResult, preview.Current.Result);
	}

	/// <summary>
	/// Test case: cancellation does not publish failed state.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task CancellationDoesNotPublishFailedState()
	{
		var factory = new TestModelFactory();
		var folder = factory.CreateModel("folder", "Folder", out _);
		var source = new TestPreviewSource
		{
			Handler = async cancellationToken =>
			{
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

				return null;
			},
		};
		var item = factory.CreateModel("item", "Item", out _, previewSource: source);
		var resolver = new TestBrowseLocationResolver([item])
		{
			LocationModelFactory = _ => folder,
		};
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(folder.Reference));
		await using var preview = new BrowsePreviewModel(session, TimeSpan.Zero);

		session.SetSelection([item.Reference.GetKey()], item.Reference.GetKey(), null);
		await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

		using var cancellation = new CancellationTokenSource();
		var refresh = preview.RefreshAsync(cancellationToken: cancellation.Token);
		cancellation.Cancel();
		await refresh;

		Assert.AreNotEqual(BrowsePreviewStatus.Failed, preview.Current.Status);
		Assert.IsNull(preview.Current.Error);
	}

	/// <summary>
	/// Test case: disposal cancels request and disposes current result.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task DisposalCancelsRequestAndDisposesCurrentResult()
	{
		var factory = new TestModelFactory();
		var folder = factory.CreateModel("folder", "Folder", out _);
		var result = new TestPreviewResult();
		var source = new TestPreviewSource { Result = result };
		var item = factory.CreateModel("item", "Item", out _, previewSource: source);
		var resolver = new TestBrowseLocationResolver([item])
		{
			LocationModelFactory = _ => folder,
		};
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(folder.Reference));
		var preview = new BrowsePreviewModel(session, TimeSpan.Zero);
		session.SetSelection([item.Reference.GetKey()], item.Reference.GetKey(), null);

		await WaitUntilAsync(() => ReferenceEquals(preview.Current.Result, result));
		await preview.DisposeAsync();

		Assert.IsTrue(result.IsDisposed);
		Assert.AreEqual(BrowsePreviewStatus.Empty, preview.Current.Status);
	}

	/// <summary>
	/// Test case: a cleanup failure from the previous result does not replace the newly published state.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task PreviousResultDisposalFailureDoesNotReplaceNewState()
	{
		var factory = new TestModelFactory();
		var folder = factory.CreateModel("folder", "Folder", out _);
		var oldResult = new ThrowingPreviewResult();
		var newResult = new TestPreviewResult();
		var first = factory.CreateModel("first", "First", out _, previewSource: new TestPreviewSource { Result = oldResult });
		var second = factory.CreateModel("second", "Second", out _, previewSource: new TestPreviewSource { Result = newResult });
		var resolver = new TestBrowseLocationResolver([first, second]) { LocationModelFactory = _ => folder };
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(folder.Reference));
		await using var preview = new BrowsePreviewModel(session, TimeSpan.Zero);

		session.SetSelection([first.Reference.GetKey()], first.Reference.GetKey(), null);
		await WaitUntilAsync(() => ReferenceEquals(preview.Current.Result, oldResult));
		session.SetSelection([second.Reference.GetKey()], second.Reference.GetKey(), null);
		await preview.RefreshAsync();

		Assert.IsTrue(oldResult.DisposeAttempted);
		Assert.AreEqual(BrowsePreviewStatus.Ready, preview.Current.Status);
		Assert.AreSame(newResult, preview.Current.Result);
	}

	/// <summary>
	/// Test case: an untrusted retry is bound to the exact blocked snapshot and selected model.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task UntrustedRetryIsBoundToExactBlockedSnapshotAndModel()
	{
		var factory = new TestModelFactory();
		var folder = factory.CreateModel("folder", "Folder", out _);
		var firstSource = new TestPreviewSource { Result = new BlockedPreviewResult(PreviewBlockReason.Untrusted) };
		var secondSource = new TestPreviewSource { Result = new TestPreviewResult() };
		var first = factory.CreateModel("first", "First", out _, previewSource: firstSource);
		var second = factory.CreateModel("second", "Second", out _, previewSource: secondSource);
		var resolver = new TestBrowseLocationResolver([first, second]) { LocationModelFactory = _ => folder };
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(folder.Reference));
		await using var preview = new BrowsePreviewModel(session, TimeSpan.Zero);

		session.SetSelection([first.Reference.GetKey()], first.Reference.GetKey(), null);
		await preview.RefreshAsync(PreviewHydrationPolicy.AllowHydration);
		var blockedSnapshot = preview.Current;
		await preview.PreviewUntrustedAsync(blockedSnapshot);

		Assert.AreEqual(PreviewTrustPolicy.AllowUntrusted, firstSource.Request?.TrustPolicy);
		Assert.AreEqual(PreviewHydrationPolicy.AllowHydration, firstSource.Request?.HydrationPolicy);

		session.SetSelection([second.Reference.GetKey()], second.Reference.GetKey(), null);
		await preview.PreviewUntrustedAsync(blockedSnapshot);
		await WaitUntilAsync(() => preview.Current.TargetKey == second.Reference.GetKey() && preview.Current.Status is BrowsePreviewStatus.Ready);

		Assert.AreEqual(PreviewTrustPolicy.Default, secondSource.Request?.TrustPolicy);
	}

	/// <summary>
	/// Test case: an untrusted retry does not authorize a silently replaced model with the same key.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task UntrustedRetryRejectsReplacementWithTheSameKey()
	{
		var factory = new TestModelFactory();
		var blockedSource = new TestPreviewSource { Result = new BlockedPreviewResult(PreviewBlockReason.Untrusted) };
		var replacementSource = new TestPreviewSource { Result = new TestPreviewResult() };
		var blockedItem = factory.CreateModel("item", "Before", out _, previewSource: blockedSource);
		var replacement = factory.CreateModel("item", "After", out _, previewSource: replacementSource);
		using var session = new MutableBrowseSession(blockedItem);
		await using var preview = new BrowsePreviewModel(session, TimeSpan.Zero);

		session.SetSelection([blockedItem.Reference.GetKey()], blockedItem.Reference.GetKey(), null);
		await WaitUntilAsync(() => preview.Current.Status is BrowsePreviewStatus.Blocked);
		var blockedSnapshot = preview.Current;
		session.ReplaceItemSilently(replacement);

		await preview.PreviewUntrustedAsync(blockedSnapshot);

		Assert.AreSame(blockedSnapshot, preview.Current);
		Assert.IsNull(replacementSource.Request);
		blockedItem.Dispose();
		replacement.Dispose();
	}

	/// <summary>
	/// Test case: an activation-time Shell policy block becomes an exact retryable snapshot.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task ActivationBlockBecomesExactRetryableSnapshot()
	{
		var factory = new TestModelFactory();
		var folder = factory.CreateModel("folder", "Folder", out _);
		var shellResult = new WindowsShellPreviewResult(new StorableReference(new StorageSourceId("source"), "item"), Guid.NewGuid());
		var source = new TestPreviewSource { Result = shellResult };
		var item = factory.CreateModel("item", "Item", out _, previewSource: source);
		var resolver = new TestBrowseLocationResolver([item]) { LocationModelFactory = _ => folder };
		using var session = new BrowseSession(resolver);
		await session.NavigateAsync(new FolderLocation(folder.Reference));
		await using var preview = new BrowsePreviewModel(session, TimeSpan.Zero);

		session.SetSelection([item.Reference.GetKey()], item.Reference.GetKey(), null);
		await WaitUntilAsync(() => preview.Current.Status is BrowsePreviewStatus.Ready);
		var readySnapshot = preview.Current;

		Assert.IsTrue(preview.TryReportShellPreviewBlocked(readySnapshot, PreviewBlockReason.Untrusted));
		Assert.AreEqual(BrowsePreviewStatus.Blocked, preview.Current.Status);
		Assert.AreEqual(PreviewBlockReason.Untrusted, preview.Current.BlockReason);
		Assert.AreSame(shellResult, preview.Current.Result);
		Assert.IsFalse(preview.TryReportShellPreviewBlocked(readySnapshot, PreviewBlockReason.Untrusted));
	}

	/// <summary>
	/// Test case: loader factory binds context to item source.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task LoaderFactoryBindsContextToItemSource()
	{
		var storageSource = new TestStorageSource();
		var coreModel = new TestStorable("item", "Item");
		var context = new ItemContext(storageSource, coreModel, new StorableReference(storageSource.SourceId, coreModel.Id));
		var loader = new TestPreviewLoader();
		var source = new PreviewSourceFactory(loader).Create(context);

		Assert.IsNotNull(source);
		await using var result = await source!.GetPreviewAsync(new PreviewRequest());

		Assert.AreSame(context, loader.Context);
		Assert.AreSame(loader.Result, result);
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
		while (!condition())
		{
			if (DateTime.UtcNow >= deadline)
			{
				Assert.Fail("The expected preview state was not reached.");
			}

			await Task.Delay(10);
		}
	}

	private sealed class TestPreviewSource : IPreviewSource
	{
		public TaskCompletionSource<bool> Started { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public TaskCompletionSource<bool> Completed { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public PreviewResult? Result { get; init; }

		public PreviewRequest? Request { get; private set; }

		public Func<CancellationToken, ValueTask<PreviewResult?>>? Handler { get; set; }

		public async ValueTask<PreviewResult?> GetPreviewAsync(PreviewRequest request, CancellationToken cancellationToken = default)
		{
			Request = request;
			Started.TrySetResult(true);
			try
			{
				return Handler is null
					? Result
					: await Handler(cancellationToken);
			}
			finally
			{
				Completed.TrySetResult(true);
			}
		}
	}

	private sealed class TestPreviewResult : PreviewResult
	{
		public bool IsDisposed { get; private set; }

		public override ValueTask DisposeAsync()
		{
			IsDisposed = true;

			return ValueTask.CompletedTask;
		}
	}

	private sealed class MutableBrowseSession : IBrowseSession
	{
		private IReadOnlyList<IStorableModel> _items;

		public BrowseLocation? Location => null;

		public IBrowseLocationContext? Context => null;

		public long Generation => 1;

		public IReadOnlyList<IStorableModel> Items => _items;

		public long ItemsVersion => 1;

		public BrowseSelectionState Selection { get; private set; } = BrowseSelectionState.Empty;

		public BrowseViewSettings ViewSettings => BrowseViewSettings.Default;

		public BrowseDisplaySettings DisplaySettings => BrowseDisplaySettings.Default;

		public bool IsLoading => false;

		public Exception? Error => null;

		public event EventHandler? StateChanged
		{
			add { }
			remove { }
		}

		public event EventHandler<BrowseItemsChangedEventArgs>? ItemsChanged
		{
			add { }
			remove { }
		}

		public event EventHandler<BrowseItemPresentationChangedEventArgs>? ItemPresentationChanged
		{
			add { }
			remove { }
		}

		public event EventHandler? SelectionChanged;

		public MutableBrowseSession(IStorableModel item)
		{
			_items = [item];
		}

		public bool Contains(StorableKey key) => _items.Any(item => item.Reference.GetKey() == key);

		public bool TryGet(StorableKey key, out IStorableModel item)
		{
			item = _items.FirstOrDefault(candidate => candidate.Reference.GetKey() == key)!;

			return item is not null;
		}

		public ValueTask NavigateAsync(BrowseLocation location, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask RefreshAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask UpdateViewSettingsAsync(BrowseViewSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask UpdateDisplaySettingsAsync(BrowseDisplaySettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public bool TryGetPresentation(StorableKey key, out BrowseItemPresentation presentation)
		{
			presentation = default!;

			return false;
		}

		public void SetSelection(IEnumerable<StorableKey> selectedKeys, StorableKey? focusedKey, StorableKey? anchorKey)
		{
			Selection = new BrowseSelectionState(selectedKeys.ToArray(), focusedKey, anchorKey);
			SelectionChanged?.Invoke(this, EventArgs.Empty);
		}

		public void ReplaceItemSilently(IStorableModel item)
		{
			_items = [item];
		}

		public void Dispose()
		{
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

	private sealed class ThrowingPreviewResult : PreviewResult
	{
		public bool DisposeAttempted { get; private set; }

		public override ValueTask DisposeAsync()
		{
			DisposeAttempted = true;

			return ValueTask.FromException(new InvalidOperationException("Expected disposal failure."));
		}
	}

	private sealed class TestPreviewLoader : IPreviewLoader
	{
		public ItemContext? Context { get; private set; }

		public TestPreviewResult Result { get; } = new();

		public bool CanLoad(ItemContext context) => true;

		public ValueTask<PreviewResult?> GetPreviewAsync(PreviewRequest request, ItemContext context, CancellationToken cancellationToken = default)
		{
			Context = context;

			return ValueTask.FromResult<PreviewResult?>(Result);
		}
	}
}

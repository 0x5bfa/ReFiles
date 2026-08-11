// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.ItemFeatures;
using Files.Core.ItemFeatures.Previews;
using Files.Core.Models;
using Files.Core.Storage;

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

		public Func<CancellationToken, ValueTask<PreviewResult?>>? Handler { get; set; }

		public async ValueTask<PreviewResult?> GetPreviewAsync(PreviewRequest request, CancellationToken cancellationToken = default)
		{
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

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using Files.Core.Browsing;
using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Storage;

namespace Files.UnitTests;

/// <summary>Verifies source isolation, retry, and ownership in Home.</summary>
[TestClass]
public sealed class HomeAggregationTests
{
	/// <summary>Fast roots become visible while another source is blocked and a third has failed.</summary>
	/// <returns>The test task.</returns>
	[TestMethod]
	public async Task SlowAndFailedSourcesDoNotBlockHealthyRoots()
	{
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var slow = new RootSource("slow") { Release = release.Task };
		var failed = new RootSource("failed") { FailuresRemaining = 1 };
		var fast = new RootSource("fast");
		await using var workspace = new StorageWorkspace([slow, failed, fast], new StorableModelFactory());
		await using var session = CreateSession(workspace);
		var visible = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		session.ItemsChanged += (_, _) =>
		{
			if (session.Items.Any(item => item.Reference.SourceId == fast.SourceId))
			{
				visible.TrySetResult();
			}
		};
		var navigation = session.NavigateAsync(HomeLocation.Instance).AsTask();
		try
		{
			await visible.Task.WaitAsync(TimeSpan.FromSeconds(5));
			Assert.IsFalse(navigation.IsCompleted);
			Assert.IsNotNull(session.Context as HomeBrowseLocationContext);
		}
		finally
		{
			release.TrySetResult();
		}

		await navigation.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.IsNull(session.Error);
		Assert.AreEqual(2, session.Items.Count);
		Assert.AreEqual(HomeSourceLoadState.Failed, session.HomeSources.Single(state => state.SourceId == failed.SourceId).State);
		Assert.IsInstanceOfType<IOException>(session.HomeSources.Single(state => state.SourceId == failed.SourceId).Error);
		Assert.AreEqual(HomeSourceLoadState.Ready, session.HomeSources.Single(state => state.SourceId == fast.SourceId).State);
	}

	/// <summary>Retry refreshes only the failed source and preserves old roots on a partial failure.</summary>
	/// <returns>The test task.</returns>
	[TestMethod]
	public async Task RetryIsSourceScopedAndPreservesRootsOnFailure()
	{
		var healthy = new RootSource("healthy");
		var remote = new RootSource("remote") { FailuresRemaining = 1 };
		await using var workspace = new StorageWorkspace([healthy, remote], new StorableModelFactory());
		await using var session = CreateSession(workspace);
		await session.NavigateAsync(HomeLocation.Instance);
		var home = (HomeBrowseLocationContext)session.Context!;
		var applied = WaitForItemsAsync(session, () => session.Items.Count == 2);
		await home.RetrySourceAsync(remote.SourceId);
		await applied.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.AreEqual(1, healthy.EnumerationCount);
		Assert.AreEqual(2, remote.EnumerationCount);
		Assert.AreEqual(HomeSourceLoadState.Ready, home.Sources.Single(state => state.SourceId == remote.SourceId).State);
		Assert.IsNull(home.Sources.Single(state => state.SourceId == remote.SourceId).Error);

		remote.RootId = "replacement";
		remote.FailAfterRoot = true;
		await home.RetrySourceAsync(remote.SourceId);
		Assert.AreEqual(HomeSourceLoadState.Failed, home.Sources.Single(state => state.SourceId == remote.SourceId).State);
		Assert.IsTrue(session.Items.Any(item => item.Reference.SourceId == remote.SourceId && item.Reference.ItemId == "root-0"));
		Assert.IsFalse(session.Items.Any(item => item.Reference.ItemId == "replacement-0"));
		Assert.AreEqual(1, healthy.EnumerationCount);

		remote.FailAfterRoot = false;
		applied = WaitForItemsAsync(session, () => session.Items.Any(item => item.Reference.ItemId == "replacement-0") && session.Items.Count == 2);
		await home.RetrySourceAsync(remote.SourceId);
		await applied.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.AreEqual(1, healthy.EnumerationCount);
	}

	/// <summary>Abandoning enumeration disposes buffered models and models still held by blocked writers.</summary>
	/// <returns>The test task.</returns>
	[TestMethod]
	public async Task AbandonedEnumerationReleasesEveryUnconsumedRoot()
	{
		var sources = new[] { new RootSource("first") { RootCount = 32 }, new RootSource("second") { RootCount = 32 } };
		await using var workspace = new StorageWorkspace(sources, new StorableModelFactory());
		await using var context = new HomeBrowseLocationContext(HomeLocation.Instance, workspace);
		var iterator = context.GetItemsAsync().GetAsyncEnumerator();
		Assert.IsTrue(await iterator.MoveNextAsync());
		await iterator.Current.DisposeAsync();
		await iterator.DisposeAsync();
		Assert.IsTrue(sources.All(source => source.LiveRoots == 0));
	}

	/// <summary>Changing visible sources refreshes Home while removed source resources stay valid for borrowed models.</summary>
	/// <returns>The test task.</returns>
	[TestMethod]
	public async Task RegistryChangesRefreshHomeWithoutPrematureSourceDisposal()
	{
		var first = new RootSource("first");
		var second = new RootSource("second");
		var workspace = new StorageWorkspace([first], new StorableModelFactory());
		await using (workspace)
		{
			await using var borrowed = await workspace.ResolveAsync(new StorableReference(first.SourceId, "root-0"));
			await using var session = CreateSession(workspace);
			await session.NavigateAsync(HomeLocation.Instance);
			var applied = WaitForItemsAsync(session, () => session.Items.Any(item => item.Reference.SourceId == second.SourceId));
			workspace.AddSource(second);
			await applied.WaitAsync(TimeSpan.FromSeconds(5));
			applied = WaitForItemsAsync(session, () => session.Items.Count == 1 && session.Items.All(item => item.Reference.SourceId == second.SourceId));
			Assert.IsTrue(workspace.RemoveSource(first.SourceId));
			await applied.WaitAsync(TimeSpan.FromSeconds(5));
			Assert.AreEqual(0, first.DisposeCount);
			Assert.AreEqual("root-0", borrowed.Name);
			var impostor = new RootSource("first");
			Assert.Throws<ArgumentException>(() => workspace.AddSource(impostor));
			Assert.AreEqual(0, impostor.DisposeCount);
			await impostor.DisposeAsync();
			workspace.AddSource(first);
			Assert.AreEqual(2, workspace.Sources.Count);
		}

		Assert.AreEqual(1, first.DisposeCount);
		Assert.AreEqual(1, second.DisposeCount);
	}

	private static BrowseSession CreateSession(IStorageWorkspace workspace)
	{
		return new BrowseSession(new BrowseLocationResolver([new HomeBrowseLocationHandler(workspace)]));
	}

	private static Task WaitForItemsAsync(BrowseSession session, Func<bool> condition)
	{
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		EventHandler<BrowseItemsChangedEventArgs>? handler = null;
		handler = (_, _) =>
		{
			if (condition())
			{
				session.ItemsChanged -= handler;
				completion.TrySetResult();
			}
		};
		session.ItemsChanged += handler;
		handler(null, null!);

		return completion.Task;
	}

	private sealed class RootSource(string id) : IStorageSource
	{
		private int _enumerationCount;
		private int _liveRoots;
		public StorageSourceId SourceId { get; } = new(id);
		public string SourceType => "test";
		public string DisplayName => id;
		public string RootId { get; set; } = "root";
		public int RootCount { get; init; } = 1;
		public int FailuresRemaining { get; set; }
		public bool FailAfterRoot { get; set; }
		public Task? Release { get; init; }
		public int EnumerationCount => Volatile.Read(ref _enumerationCount);
		public int LiveRoots => Volatile.Read(ref _liveRoots);
		public int DisposeCount { get; private set; }

		public async IAsyncEnumerable<IFolder> GetRootsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			Interlocked.Increment(ref _enumerationCount);
			if (Release is { } release)
			{
				await release.WaitAsync(cancellationToken);
			}

			if (FailuresRemaining > 0)
			{
				FailuresRemaining--;
				throw new IOException("Source unavailable");
			}

			for (var index = 0; index < RootCount; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				yield return CreateRoot($"{RootId}-{index}");
				if (FailAfterRoot)
				{
					throw new IOException("Source failed after a partial listing");
				}
			}
		}

		public bool CanResolve(StorageAddress address) => false;

		public ValueTask<IStorable> ResolveAsync(StorageAddress address, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask<IStorable> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
		{
			return ValueTask.FromResult<IStorable>(CreateRoot(reference.ItemId));
		}

		public ValueTask DisposeAsync()
		{
			DisposeCount++;

			return ValueTask.CompletedTask;
		}

		private RootFolder CreateRoot(string rootId)
		{
			Interlocked.Increment(ref _liveRoots);

			return new RootFolder(rootId, () => Interlocked.Decrement(ref _liveRoots));
		}
	}

	private sealed class RootFolder(string id, Action release) : IFolder, IDisposable
	{
		public string Id => id;
		public string Name => id;
		public async IAsyncEnumerable<IStorableChild> GetItemsAsync(StorableType type = StorableType.All, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			await Task.CompletedTask;
			yield break;
		}

		public void Dispose() => release();
	}
}

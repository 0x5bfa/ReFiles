// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.Browsing;
using Files.Core.Capabilities.Previews;
using Files.Infrastructure;
using Files.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.UITests;

/// <summary>
/// Verifies preview pane state publication and version handling.
/// </summary>
[TestClass]
public sealed class PreviewPaneViewModelTests
{
	/// <summary>
	/// Verifies that the latest preview state is published to the view model.
	/// </summary>
	[TestMethod]
	public void PublishesTheLatestPreviewState()
	{
		var preview = new TestPreviewModel();
		using var viewModel = new PreviewPaneViewModel(preview, new InlineDispatcher());
		var result = new BlockedPreviewResult(PreviewBlockReason.TooLarge);

		preview.Publish(new BrowsePreviewSnapshot(2, null, BrowsePreviewStatus.Blocked, result, PreviewBlockReason.TooLarge));

		Assert.AreEqual(BrowsePreviewStatus.Blocked, viewModel.Status);
		Assert.AreSame(result, viewModel.Result);
		Assert.IsFalse(viewModel.HasContent);
	}

	/// <summary>
	/// Verifies that an older preview state does not replace a newer state.
	/// </summary>
	[TestMethod]
	public void IgnoresAnOlderPreviewState()
	{
		var preview = new TestPreviewModel();
		using var viewModel = new PreviewPaneViewModel(preview, new InlineDispatcher());

		preview.Publish(new BrowsePreviewSnapshot(3, null, BrowsePreviewStatus.Unavailable));
		preview.Publish(new BrowsePreviewSnapshot(2, null, BrowsePreviewStatus.Loading));

		Assert.AreEqual(BrowsePreviewStatus.Unavailable, viewModel.Status);
		Assert.AreEqual(3, viewModel.Snapshot.RequestVersion);
	}

	/// <summary>
	/// Verifies that a stream remains alive until a queued snapshot change reaches the UI thread.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task RetainsStreamUntilQueuedSnapshotIsApplied()
	{
		var preview = new TestPreviewModel();
		var stream = new MemoryStream();
		var result = new StreamPreviewResult(stream, "text/plain");
		preview.Publish(new BrowsePreviewSnapshot(1, null, BrowsePreviewStatus.Ready, result));
		var dispatcher = new ManualDispatcher();
		using var viewModel = new PreviewPaneViewModel(preview, dispatcher);

		preview.Publish(new BrowsePreviewSnapshot(2, null, BrowsePreviewStatus.Empty));
		var disposal = result.DisposeAsync().AsTask();

		Assert.IsFalse(disposal.IsCompleted);
		dispatcher.RunNext();
		await disposal;

		Assert.Throws<ObjectDisposedException>(() => result.AcquireContent());
	}

	/// <summary>
	/// Verifies that disposal releases a stream retained by a dispatcher callback that has not run.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task DisposalReleasesStreamRetainedByQueuedSnapshot()
	{
		var preview = new TestPreviewModel();
		var dispatcher = new ManualDispatcher();
		var viewModel = new PreviewPaneViewModel(preview, dispatcher);
		var result = new StreamPreviewResult(new MemoryStream(), "text/plain");

		preview.Publish(new BrowsePreviewSnapshot(1, null, BrowsePreviewStatus.Ready, result));
		var disposal = result.DisposeAsync().AsTask();
		Assert.IsFalse(disposal.IsCompleted);

		viewModel.Dispose();
		await disposal;

		Assert.Throws<ObjectDisposedException>(() => result.AcquireContent());
	}

	/// <summary>
	/// Verifies that only an untrusted block enables an exact-snapshot retry.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task UntrustedBlockEnablesExactSnapshotRetry()
	{
		var preview = new TestPreviewModel();
		using var viewModel = new PreviewPaneViewModel(preview, new InlineDispatcher());
		var snapshot = new BrowsePreviewSnapshot(1, null, BrowsePreviewStatus.Blocked, new BlockedPreviewResult(PreviewBlockReason.Untrusted), PreviewBlockReason.Untrusted);

		preview.Publish(snapshot);
		await viewModel.PreviewUntrustedAsync();

		Assert.IsTrue(viewModel.CanPreviewUntrusted);
		Assert.AreSame(snapshot, preview.RetrySnapshot);
	}

	/// <summary>
	/// Verifies that an activation-time Shell policy failure is reported against the exact rendered snapshot.
	/// </summary>
	[TestMethod]
	public void ShellActivationBlockUpdatesTheCurrentSnapshot()
	{
		var preview = new TestPreviewModel();
		using var viewModel = new PreviewPaneViewModel(preview, new InlineDispatcher());
		var shellResult = new WindowsShellPreviewResult(new Files.Core.Storage.StorableReference(new Files.Core.Storage.StorageSourceId("source"), "item"), Guid.NewGuid());
		var snapshot = new BrowsePreviewSnapshot(1, null, BrowsePreviewStatus.Ready, shellResult);

		preview.Publish(snapshot);

		Assert.IsTrue(viewModel.TryReportShellPreviewBlocked(snapshot, PreviewBlockReason.Untrusted));
		Assert.AreEqual(BrowsePreviewStatus.Blocked, viewModel.Status);
		Assert.IsTrue(viewModel.CanPreviewUntrusted);
		Assert.AreSame(shellResult, viewModel.Result);
	}

	private sealed class TestPreviewModel : IBrowsePreviewModel
	{
		public BrowsePreviewSnapshot Current { get; private set; } = new(0, null, BrowsePreviewStatus.Empty);

		public BrowsePreviewSnapshot? RetrySnapshot { get; private set; }

		public event EventHandler? Changed;

		public ValueTask RefreshAsync(PreviewHydrationPolicy hydrationPolicy = PreviewHydrationPolicy.LocalOnly, CancellationToken cancellationToken = default)
		{
			return ValueTask.CompletedTask;
		}

		public ValueTask PreviewUntrustedAsync(BrowsePreviewSnapshot blockedSnapshot, CancellationToken cancellationToken = default)
		{
			RetrySnapshot = blockedSnapshot;

			return ValueTask.CompletedTask;
		}

		public bool TryReportShellPreviewBlocked(BrowsePreviewSnapshot expectedSnapshot, PreviewBlockReason reason)
		{
			if (!ReferenceEquals(Current, expectedSnapshot) || expectedSnapshot.Result is not WindowsShellPreviewResult)
			{
				return false;
			}

			Publish(expectedSnapshot with { Status = BrowsePreviewStatus.Blocked, BlockReason = reason });

			return true;
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;

		public void Publish(BrowsePreviewSnapshot snapshot)
		{
			Current = snapshot;
			Changed?.Invoke(this, EventArgs.Empty);
		}
	}

	private sealed class InlineDispatcher : IUIDispatcher
	{
		public bool HasThreadAccess => true;

		public bool TryEnqueue(Action callback)
		{
			callback();

			return true;
		}

		public bool TryEnqueue(DispatcherQueuePriority priority, Action callback)
		{
			callback();

			return true;
		}
	}

	private sealed class ManualDispatcher : IUIDispatcher
	{
		private readonly Queue<Action> _callbacks = [];

		public bool HasThreadAccess => false;

		public bool TryEnqueue(Action callback)
		{
			_callbacks.Enqueue(callback);

			return true;
		}

		public bool TryEnqueue(DispatcherQueuePriority priority, Action callback)
		{
			return TryEnqueue(callback);
		}

		public void RunNext()
		{
			_callbacks.Dequeue()();
		}
	}
}

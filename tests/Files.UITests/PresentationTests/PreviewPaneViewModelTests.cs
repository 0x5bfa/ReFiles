// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

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

	private sealed class TestPreviewModel : IBrowsePreviewModel
	{
		public BrowsePreviewSnapshot Current { get; private set; } = new(0, null, BrowsePreviewStatus.Empty);

		public event EventHandler? Changed;

		public ValueTask RefreshAsync(PreviewHydrationPolicy hydrationPolicy = PreviewHydrationPolicy.LocalOnly, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

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
}

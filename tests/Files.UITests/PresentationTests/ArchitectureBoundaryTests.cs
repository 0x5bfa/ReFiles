// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Files.Adapters;
using Files.Commands;
using Files.Core.Browsing;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Changes;
using Files.Core;
using Files.Core.Models;
using Files.Core.Sessions;
using Files.Core.Storage;
using Files.Infrastructure;
using Files.Presentation;
using Files.Settings;
using Files.StorageOperations;
using Files.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;

namespace Files.UITests;

/// <summary>Verifies model-to-presentation boundaries and stable keyed rows.</summary>
[TestClass]
public sealed class ArchitectureBoundaryTests
{
	/// <summary>A new content kind renders through registration without a PaneHost branch.</summary>
	[UITestMethod]
	public async Task CustomContentUsesItsRegisteredPresenter()
	{
		await using var runtime = new FilesCoreBuilder().Build();
		var window = await runtime.ShellSession.CreateWindowAsync();
		using var tracker = new StorageOperationTracker();
		using var settings = new AppSettingsService(new AppSettingsData());
		var dispatcher = new DispatcherQueueUIDispatcher(UnitTestApp.TestDispatcherQueue);
		var factory = new WindowPresentationFactory(runtime.Workspace, runtime.StorageOperations, tracker, settings, dispatcher, AppCommandRegistration.Build());
		var createdViews = 0;
		factory.RegisterPaneContent<CustomContent>((content, _) => new PaneContentPresentation(content, () =>
		{
			createdViews++;

			return new TextBlock { Text = "Custom content" };
		}, title: "Custom title"));
		await using var root = factory.Create(window);
		await using var pane = new PaneSession(new CustomContent());
		await using var viewModel = factory.CreatePane(pane, root.Commands);
		var view = new PaneContentView { ViewModel = viewModel };
		var presenter = (ContentPresenter)view.Content;

		Assert.AreEqual("Custom title", viewModel.Title);
		Assert.IsNull(viewModel.FolderBrowser);
		Assert.IsNull(viewModel.Preview);
		Assert.AreEqual("Custom content", ((TextBlock)presenter.Content).Text);
		view.ViewModel = viewModel;

		Assert.AreEqual(1, createdViews);

		var tab = await window.OpenContentTabAsync(() => new CustomContent());
		var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Assert.IsTrue(dispatcher.TryEnqueue(() => refreshed.SetResult()));
		await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await root.CloseTabAsync(tab.Id);

		Assert.IsFalse(window.Tabs.Any(current => current.Id == tab.Id));
		Assert.IsFalse(root.CanReopenTab);
	}

	/// <summary>Replacing an immutable model with the same identity updates its existing UI row.</summary>
	[TestMethod]
	public async Task SameIdentityReplacementKeepsTheExistingRow()
	{
		var context = new MutableContext();
		var session = new BrowseSession(new MutableResolver(context));
		await using var pane = new BrowsePaneSession(session, new BrowsePreviewModel(session));
		var dispatcher = new QueuedDispatcher();
		await using var adapter = new BrowsePresentationAdapter(pane, dispatcher, new NoPrefetch(), new BrowsePresentationText("Home", new BrowseStatusBarText("{0} item", "{0} items", "{0} selected",
			"{0} selected", "{0} {1}", "{0} {1}", (string[])["B", "KB"]), "{0} is not a folder"));
		await adapter.InitializeAsync();
		dispatcher.Drain();
		var original = adapter.Items.Single();
		var replacementApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		session.ItemsChanged += (_, args) =>
		{
			if (args.Changes.Any(static change => change is BrowseItemReplaced))
			{
				replacementApplied.TrySetResult();
			}
		};

		context.Replace("After");
		await replacementApplied.Task.WaitAsync(TimeSpan.FromSeconds(5));
		dispatcher.Drain();

		Assert.AreSame(original, adapter.Items.Single());
		Assert.AreEqual("After", original.Name);
		Assert.AreEqual("After", original.ReferenceText);
	}

	/// <summary>Core clients can distinguish requested controls from backend acknowledgements.</summary>
	[TestMethod]
	public void OperationMonitorPreservesControlAcknowledgements()
	{
		using var controller = new TestStorageOperationController();
		IStorageOperationMonitor monitor = controller;
		var operation = controller.StartOperation(TrackedStorageOperationKind.Copy, 2, "one.txt", canCancel: true, canPause: true);
		var id = monitor.GetOperationStates().Single().Id;

		Assert.IsTrue(monitor.SetPaused(id, true));
		Assert.AreEqual(StorageJobState.Pausing, monitor.GetOperationStates().Single().State);
		operation.AcknowledgePauseState(true);
		Assert.AreEqual(StorageJobState.Paused, monitor.GetOperationStates().Single().State);
		Assert.IsTrue(monitor.Cancel(id));
		var pending = monitor.GetOperationStates().Single();
		Assert.IsTrue(pending.IsCancellationRequested);
		Assert.IsFalse(pending.IsCancellationAcknowledged);
		operation.AcknowledgeCancellationRequest();
		operation.MarkCanceled();

		Assert.AreEqual(StorageJobState.Canceled, monitor.GetOperationStates().Single().State);
		Assert.IsTrue(monitor.GetOperationStates().Single().IsCancellationAcknowledged);
	}

	private sealed class CustomContent : IPaneContentSession
	{
		public ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}
	}

	private sealed class QueuedDispatcher : IUIDispatcher
	{
		private readonly ConcurrentQueue<Action> _callbacks = new();
		public bool HasThreadAccess => true;

		public bool TryEnqueue(Action callback)
		{
			_callbacks.Enqueue(callback);

			return true;
		}

		public bool TryEnqueue(DispatcherQueuePriority priority, Action callback)
		{
			return TryEnqueue(callback);
		}

		public void Drain()
		{
			while (_callbacks.TryDequeue(out var callback))
			{
				callback();
			}
		}
	}

	private sealed class NoPrefetch : IBrowsePrefetchCoordinator
	{
		public void UpdateViewport(BrowseViewport viewport, Files.Core.ViewSettings.BrowseViewSettings settings, long generation)
		{
		}

		public ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}
	}

	private sealed class MutableResolver(MutableContext context) : IBrowseLocationResolver
	{
		public ValueTask<IBrowseLocationContext> OpenAsync(BrowseLocation location, CancellationToken cancellationToken = default)
		{
			return ValueTask.FromResult<IBrowseLocationContext>(context);
		}
	}

	private sealed class MutableContext : IBrowseLocationContext, IBrowseLocationItemResolver
	{
		private readonly StubSource _source = new();
		private readonly Changes _changes = new();
		private readonly StorableModelFactory _factory = new();
		private IStorableModel _current;

		public BrowseLocation Location => HomeLocation.Instance;
		public IStorableModel LocationModel { get; }

		public MutableContext()
		{
			var root = new StubItem("root", "Root");
			var reference = new StorableReference(_source.SourceId, root.Id);
			var registry = new CapabilityBuilder().Add<IFolderChangeSource>(new DelegateCapabilityFactory<IFolderChangeSource>(_ => _changes)).Build();
			LocationModel = new StorableModel(root, reference, registry.CreateCapabilities(new ItemContext(_source, root, reference)));
			_current = _factory.Create(_source, new StubItem("item", "Before"));
		}

		public async IAsyncEnumerable<IStorableModel> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			await Task.CompletedTask;

			yield return _current;
		}

		public ValueTask<IStorableModel> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
		{
			return ValueTask.FromResult(_current);
		}

		public void Replace(string name)
		{
			_current = _factory.Create(_source, new StubItem("item", name));
			_changes.Publish(new FolderChange(FolderChangeKind.Updated, _current.Reference, null, RequiresRefresh: false));
		}

		public ValueTask DisposeAsync()
		{
			return LocationModel.DisposeAsync();
		}
	}

	private sealed class Changes : IFolderChangeSource
	{
		public event EventHandler<FolderChangeEventArgs>? Changed;
		public event EventHandler<FolderChangeErrorEventArgs>? Faulted { add { } remove { } }

		public void Publish(FolderChange change)
		{
			Changed?.Invoke(this, new FolderChangeEventArgs(change));
		}

		public ValueTask StartAsync(CancellationToken cancellationToken = default)
		{
			return ValueTask.CompletedTask;
		}

		public void Dispose()
		{
		}

		public ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}
	}

	private sealed record StubItem(string Id, string Name) : IStorable, IStorageAddressSource
	{
		public StorageAddress Address => new("test", Name);
	}

	private sealed class StubSource : IStorageSource
	{
		public StorageSourceId SourceId { get; } = new("architecture-test");
		public string SourceType => "test";
		public string DisplayName => "Test";

		public async IAsyncEnumerable<IFolder> GetRootsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			await Task.CompletedTask;

			yield break;
		}

		public bool CanResolve(StorageAddress address)
		{
			return false;
		}

		public ValueTask<IStorable> ResolveAsync(StorageAddress address, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public ValueTask<IStorable> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}
	}
}

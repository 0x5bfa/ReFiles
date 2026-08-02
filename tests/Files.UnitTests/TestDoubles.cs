// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using Files.Core.Browsing;
using Files.Core.ItemFeatures;
using Files.Core.ItemFeatures.Changes;
using Files.Core.ItemFeatures.Previews;
using Files.Core.ItemFeatures.Properties;
using Files.Core.ItemFeatures.Thumbnails;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.ViewSettings;
using OwlCore.Storage;

namespace Files.UnitTests;

internal sealed class TestStorageSource : IStorageSource
{
	public StorageSourceId SourceId { get; } = new("test");

	public string SourceType => "test";

	public string DisplayName => "Test";

	public bool IsDisposed { get; private set; }

	public int DisposeCount { get; private set; }

	public async IAsyncEnumerable<IFolder> GetRootsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		await Task.CompletedTask.ConfigureAwait(false);
		yield break;
	}

	public bool CanResolve(StorageAddress address) => false;

	public ValueTask<IStorable> ResolveAsync(StorageAddress address, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public ValueTask<IStorable> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public ValueTask DisposeAsync()
	{
		DisposeCount++;
		IsDisposed = true;
		return ValueTask.CompletedTask;
	}
}

internal class TestStorable : IStorable
{
	public TestStorable(string id, string name)
	{
		Id = id;
		Name = name;
	}

	public string Id { get; }

	public string Name { get; }
}

internal sealed class DisposableStorable : TestStorable, IDisposable
{
	public DisposableStorable(string id, string name)
		: base(id, name)
	{
	}

	public bool IsDisposed { get; private set; }

	public int DisposeCount { get; private set; }

	public void Dispose()
	{
		DisposeCount++;
		IsDisposed = true;
	}
}

internal sealed class TestItemFeature : IDisposable
{
	private readonly IList<string> disposalOrder;

	public TestItemFeature(string name, IList<string> disposalOrder)
	{
		Name = name;
		this.disposalOrder = disposalOrder;
	}

	public string Name { get; }

	public bool IsDisposed { get; private set; }

	public void Dispose()
	{
		if (IsDisposed)
		{
			return;
		}

		IsDisposed = true;
		disposalOrder.Add(Name);
	}
}

internal sealed class TestModelFactory
{
	private readonly TestStorageSource source = new();

	public TestStorageSource Source => source;

	public StorableModel CreateModel(
		string id,
		string name,
		out DisposableStorable coreModel,
		IFolderChangeSource? changeSource = null,
		IPropertySource? propertySource = null,
		IThumbnailSource? thumbnailSource = null,
		IPreviewSource? previewSource = null)
	{
		coreModel = new DisposableStorable(id, name);
		var reference = new StorableReference(source.SourceId, coreModel.Id, new StorageAddress("test", coreModel.Id));
		var context = new Files.Core.ItemFeatures.ItemContext(source, coreModel, reference);
		var featureBuilder = new ItemFeatureBuilder();
		if (changeSource is not null)
		{
			featureBuilder.Add<IFolderChangeSource>(new DelegateItemFeatureFactory<IFolderChangeSource>(_ => changeSource));
		}

		if (propertySource is not null)
		{
			featureBuilder.Add<IPropertySource>(new DelegateItemFeatureFactory<IPropertySource>(_ => propertySource));
		}

		if (thumbnailSource is not null)
		{
			featureBuilder.Add<IThumbnailSource>(new DelegateItemFeatureFactory<IThumbnailSource>(_ => thumbnailSource));
		}

		if (previewSource is not null)
		{
			featureBuilder.Add<IPreviewSource>(new DelegateItemFeatureFactory<IPreviewSource>(_ => previewSource));
		}

		var featureRegistry = changeSource is null
			&& propertySource is null
			&& thumbnailSource is null
			&& previewSource is null
			? ItemFeatureRegistry.Empty
			: featureBuilder.Build();

		return new StorableModel(coreModel, reference, featureRegistry.CreateFeatures(context));
	}
}

internal sealed class TestPropertySource : IPropertySource
{
	public int CallCount { get; private set; }

	public IList<IReadOnlyList<string>> Requests { get; } = [];

	public Func<
		PropertyRequest,
		CancellationToken,
		ValueTask<IReadOnlyDictionary<string, object?>>>? Handler { get; set; }

	public ValueTask<IReadOnlyDictionary<string, object?>> GetPropertiesAsync(PropertyRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		CallCount++;
		Requests.Add(request.PropertyIds);
		return Handler is null
			? ValueTask.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>())
			: Handler(request, cancellationToken);
	}
}

internal sealed class TestThumbnailSource : IThumbnailSource
{
	public int CallCount { get; private set; }

	public IList<ThumbnailRequest> Requests { get; } = [];

	public Func<ThumbnailRequest, CancellationToken, ValueTask<ThumbnailResult?>>? Handler { get; set; }

	public ValueTask<ThumbnailResult?> GetThumbnailAsync(ThumbnailRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		CallCount++;
		Requests.Add(request);
		return Handler is null
			? ValueTask.FromResult<ThumbnailResult?>(null)
			: Handler(request, cancellationToken);
	}
}

internal sealed class TestBrowseLocationResolver : IBrowseLocationResolver
{
	public TestBrowseLocationResolver(IEnumerable<IStorableModel> items, Exception? exception = null)
	{
		Items = items.ToList();
		Exception = exception;
	}

	public IList<IStorableModel> Items { get; }

	public Exception? Exception { get; set; }

	public IList<TestBrowseLocationContext> OpenedContexts { get; } = [];

	public TaskCompletionSource<bool>? EnumerationStarted { get; set; }

	public bool BlockEnumeration { get; set; }

	public TaskCompletionSource<bool>? EnumerationRelease { get; set; }

	public Func<BrowseLocation, IStorableModel?>? LocationModelFactory { get; set; }

	public Func<bool>? EnumerationGuard { get; set; }

	public Action? EnumerationAction { get; set; }

	public Action<TestBrowseLocationContext>? ContextOpened { get; set; }

	public Func<StorableReference, CancellationToken, ValueTask<IStorableModel>>? ItemResolver { get; set; }

	public ValueTask<IBrowseLocationContext> OpenAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(location);
		cancellationToken.ThrowIfCancellationRequested();

		var context = new TestBrowseLocationContext(
			location,
			Items.ToArray(),
			Exception,
			EnumerationStarted,
			BlockEnumeration,
			EnumerationRelease,
			LocationModelFactory?.Invoke(location),
			EnumerationGuard,
			EnumerationAction,
			ItemResolver);
		OpenedContexts.Add(context);
		ContextOpened?.Invoke(context);
		return ValueTask.FromResult<IBrowseLocationContext>(context);
	}
}

internal sealed class TestBrowseLocationContext :
	IBrowseLocationContext,
	IBrowseLocationItemResolver
{
	private readonly IReadOnlyList<IStorableModel> items;
	private readonly Exception? exception;
	private readonly TaskCompletionSource<bool>? enumerationStarted;
	private readonly bool blockEnumeration;
	private readonly TaskCompletionSource<bool>? enumerationRelease;
	private readonly IStorableModel? locationModel;
	private readonly Func<bool>? enumerationGuard;
	private readonly Action? enumerationAction;
	private readonly Func<StorableReference, CancellationToken, ValueTask<IStorableModel>>? itemResolver;
	private int isDisposed;

	public TestBrowseLocationContext(
		BrowseLocation location,
		IReadOnlyList<IStorableModel> items,
		Exception? exception,
		TaskCompletionSource<bool>? enumerationStarted,
		bool blockEnumeration,
		TaskCompletionSource<bool>? enumerationRelease,
		IStorableModel? locationModel,
		Func<bool>? enumerationGuard,
		Action? enumerationAction,
		Func<StorableReference, CancellationToken, ValueTask<IStorableModel>>? itemResolver)
	{
		Location = location;
		this.items = items;
		this.exception = exception;
		this.enumerationStarted = enumerationStarted;
		this.blockEnumeration = blockEnumeration;
		this.enumerationRelease = enumerationRelease;
		this.locationModel = locationModel;
		this.enumerationGuard = enumerationGuard;
		this.enumerationAction = enumerationAction;
		this.itemResolver = itemResolver;
	}

	public BrowseLocation Location { get; }

	public IStorableModel? LocationModel => locationModel;

	public bool IsDisposed => Volatile.Read(ref isDisposed) != 0;

	public ValueTask<IStorableModel> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(reference);
		return itemResolver is null
			? throw new NotSupportedException()
			: itemResolver(reference, cancellationToken);
	}

	public async IAsyncEnumerable<IStorableModel> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(IsDisposed, this);
		enumerationStarted?.TrySetResult(true);
		if (enumerationGuard is not null && !enumerationGuard())
		{
			throw new InvalidOperationException("The enumeration started before the watcher.");
		}

		enumerationAction?.Invoke();

		if (blockEnumeration)
		{
			if (enumerationRelease is null)
			{
				await Task
					.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
					.ConfigureAwait(false);
			}
			else
			{
				await enumerationRelease.Task
					.WaitAsync(cancellationToken)
					.ConfigureAwait(false);
			}
		}

		foreach (var item in items)
		{
			cancellationToken.ThrowIfCancellationRequested();
			yield return item;
			await Task.Yield();
		}

		if (exception is not null)
		{
			throw exception;
		}
	}

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref isDisposed, 1) == 0)
		{
			locationModel?.Dispose();
		}

		return ValueTask.CompletedTask;
	}
}

internal sealed class TestFolderChangeSource : IFolderChangeSource
{
	private int isDisposed;

	public event EventHandler<FolderChangeEventArgs>? Changed;

	public event EventHandler<FolderChangeErrorEventArgs>? Faulted;

	public bool IsStarted { get; private set; }

	public bool IsDisposed => Volatile.Read(ref isDisposed) != 0;

	public int StartCount { get; private set; }

	public ValueTask StartAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(IsDisposed, this);
		cancellationToken.ThrowIfCancellationRequested();
		IsStarted = true;
		StartCount++;
		return ValueTask.CompletedTask;
	}

	public void RaiseChange()
	{
		RaiseChange(new FolderChange(FolderChangeKind.Updated, null, null, RequiresRefresh: false));
	}

	public void RaiseChange(FolderChange change)
	{
		ArgumentNullException.ThrowIfNull(change);
		Changed?.Invoke(this, new FolderChangeEventArgs(change));
	}

	public void RaiseFault(Exception error)
	{
		Faulted?.Invoke(this, new FolderChangeErrorEventArgs(error));
	}

	public void Dispose()
	{
		Interlocked.Exchange(ref isDisposed, 1);
		IsStarted = false;
		Changed = null;
		Faulted = null;
	}

	public ValueTask DisposeAsync()
	{
		Dispose();
		return ValueTask.CompletedTask;
	}
}

internal sealed class TestThumbnailCache : IThumbnailCache
{
	private long invalidationVersion;

	public IList<StorableReference> InvalidatedReferences { get; } = [];

	public ValueTask<ThumbnailCacheEntry?> GetAsync(ThumbnailCacheKey key, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult<ThumbnailCacheEntry?>(null);

	public ValueTask SetAsync(ThumbnailCacheKey key, ThumbnailCacheEntry entry, CancellationToken cancellationToken = default)
		=> ValueTask.CompletedTask;

	public ValueTask<long> GetInvalidationVersionAsync(StorableReference reference, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Volatile.Read(ref invalidationVersion));

	public ValueTask<bool> TrySetAsync(
		ThumbnailCacheKey key,
		ThumbnailCacheEntry entry,
		long expectedInvalidationVersion,
		CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(expectedInvalidationVersion == Volatile.Read(ref invalidationVersion));

	public ValueTask InvalidateAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		InvalidatedReferences.Add(reference);
		Interlocked.Increment(ref invalidationVersion);
		return ValueTask.CompletedTask;
	}
}

internal sealed class TestViewSettingsStore : IViewSettingsStore
{
	private readonly Dictionary<BrowseLocation, BrowseViewSettings> values = [];

	public ValueTask<BrowseViewSettings?> GetAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return ValueTask.FromResult(values.GetValueOrDefault(location));
	}

	public ValueTask SetAsync(BrowseLocation location, BrowseViewSettings settings, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		values[location] = settings;
		return ValueTask.CompletedTask;
	}
}

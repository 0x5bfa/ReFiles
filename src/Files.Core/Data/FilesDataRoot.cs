// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Files.Core.Models;
using Files.Core.Storage;

namespace Files.Core.Data;

/// <summary>
/// Composes configured storage sources and owns their lifetime.
/// </summary>
public sealed class FilesDataRoot : IFilesDataRoot
{
	private readonly IReadOnlyDictionary<StorageSourceId, IStorageSource> sourcesById;
	private readonly object disposalLock = new();
	private Task? disposeTask;
	private volatile bool isDisposed;

	public FilesDataRoot(IEnumerable<IStorageSource> sources, IStorableModelFactory modelFactory)
	{
		ArgumentNullException.ThrowIfNull(sources);
		ArgumentNullException.ThrowIfNull(modelFactory);

		var sourceList = sources.ToArray();
		var sourceMap = new Dictionary<StorageSourceId, IStorageSource>();

		foreach (var source in sourceList)
		{
			ArgumentNullException.ThrowIfNull(source);

			if (!sourceMap.TryAdd(source.SourceId, source))
			{
				throw new ArgumentException($"A storage source with ID '{source.SourceId}' was supplied more than once.", nameof(sources));
			}
		}

		Sources = Array.AsReadOnly(sourceList);
		sourcesById = new ReadOnlyDictionary<StorageSourceId, IStorageSource>(sourceMap);
		ModelFactory = modelFactory;
	}

	public IReadOnlyList<IStorageSource> Sources { get; }

	public IStorableModelFactory ModelFactory { get; }

	public IStorageSource GetSource(StorageSourceId sourceId)
	{
		ObjectDisposedException.ThrowIf(isDisposed, this);
		ArgumentNullException.ThrowIfNull(sourceId);

		if (!sourcesById.TryGetValue(sourceId, out var source))
		{
			throw new KeyNotFoundException($"Storage source '{sourceId}' is not registered.");
		}

		return source;
	}

	public async IAsyncEnumerable<IFolderModel> GetRootsAsync(
		StorageSourceId sourceId,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var source = GetSource(sourceId);

		await foreach (var root in source.GetRootsAsync(cancellationToken).ConfigureAwait(false))
		{
			var model = ModelFactory.Create(source, root);

			if (model is not IFolderModel folderModel)
			{
				await model.DisposeAsync().ConfigureAwait(false);
				throw new InvalidOperationException($"Storage source '{source.SourceId}' returned a root that is not a folder.");
			}

			yield return folderModel;
		}
	}

	public async ValueTask<IStorableModel> ResolveAsync(StorageSourceId sourceId, StorageAddress address, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(address);

		var source = GetSource(sourceId);
		if (!source.CanResolve(address))
		{
			throw new ArgumentException($"Storage source '{sourceId}' cannot resolve address scheme '{address.Scheme}'.", nameof(address));
		}

		var coreModel = await source
			.ResolveAsync(address, cancellationToken)
			.ConfigureAwait(false);
		return ModelFactory.Create(source, coreModel);
	}

	public ValueTask<IStorableModel> ResolveAsync(StorageAddress address, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(isDisposed, this);
		ArgumentNullException.ThrowIfNull(address);

		var candidates = Sources
			.Where(source => source.CanResolve(address))
			.Take(2)
			.ToArray();

		return candidates.Length switch
		{
			0 => ValueTask.FromException<IStorableModel>(new KeyNotFoundException($"No storage source can resolve address scheme '{address.Scheme}'.")),
			1 => ResolveAsync(candidates[0].SourceId, address, cancellationToken),
			_ => ValueTask.FromException<IStorableModel>(
				new InvalidOperationException($"More than one storage source can resolve address scheme '{address.Scheme}'. Specify a source ID.")),
		};
	}

	public async ValueTask<IStorableModel> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(reference);

		var source = GetSource(reference.SourceId);
		var coreModel = await source.ResolveAsync(reference, cancellationToken).ConfigureAwait(false);
		return ModelFactory.Create(source, coreModel);
	}

	public ValueTask DisposeAsync()
	{
		lock (disposalLock)
		{
			if (disposeTask is not null)
			{
				return new ValueTask(disposeTask);
			}

			isDisposed = true;
			disposeTask = DisposeCoreAsync();
			return new ValueTask(disposeTask);
		}
	}

	private async Task DisposeCoreAsync()
	{
		List<Exception>? errors = null;
		foreach (var source in Sources.Reverse())
		{
			try
			{
				await source.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception error)
			{
				(errors ??= []).Add(error);
			}
		}

		GC.SuppressFinalize(this);

		if (errors is { Count: 1 })
		{
			throw errors[0];
		}

		if (errors is { Count: > 1 })
		{
			throw new AggregateException("One or more storage sources could not be disposed.", errors);
		}
	}

}

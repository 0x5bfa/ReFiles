// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Archives;
using OwlCore.Storage;

namespace Files.Core.Browsing;

/// <summary>
/// Mounts an archive before publishing any of its entries to a browse session.
/// </summary>
public sealed class ArchiveBrowseLocationHandler : IBrowseLocationHandler
{
	private const int _maximumCredentialAttempts = 5;
	private readonly IStorageWorkspace _workspace;
	private readonly IStorableModelFactory _modelFactory;
	private readonly ArchiveBackendSelector _backendSelector;
	private readonly IArchiveCredentialResolver? _credentialResolver;

	/// <summary>Initializes an archive browse location handler.</summary>
	/// <param name="workspace">The storage workspace.</param>
	/// <param name="modelFactory">The internal model factory for mounted entries.</param>
	/// <param name="backendSelector">The archive backend selector.</param>
	/// <param name="credentialResolver">The optional credential resolver.</param>
	public ArchiveBrowseLocationHandler(IStorageWorkspace workspace, IStorableModelFactory modelFactory, ArchiveBackendSelector backendSelector, IArchiveCredentialResolver? credentialResolver = null)
	{
		ArgumentNullException.ThrowIfNull(workspace);
		ArgumentNullException.ThrowIfNull(modelFactory);
		ArgumentNullException.ThrowIfNull(backendSelector);

		_workspace = workspace;
		_modelFactory = modelFactory;
		_backendSelector = backendSelector;
		_credentialResolver = credentialResolver;
	}

	/// <inheritdoc />
	public bool CanHandle(BrowseLocation location)
		=> location is ArchiveLocation;

	/// <inheritdoc />
	public async ValueTask<IBrowseLocationContext> OpenAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		if (location is not ArchiveLocation archiveLocation)
		{
			throw new ArgumentException("The location must identify an archive.", nameof(location));
		}

		var archiveModel = await _workspace.ResolveAsync(archiveLocation.Archive, cancellationToken).ConfigureAwait(false);
		IArchiveMount? mount = null;
		IStorableModel? locationModel = null;

		try
		{
			var source = GetSource(archiveLocation.Archive.SourceId);
			ArchiveCredential? credential = null;
			var credentialAttempt = 0;
			var credentialPromptCount = 0;

			while (true)
			{
				var request = new ArchiveMountRequest(source, archiveModel, credential, credentialAttempt, _credentialResolver);
				var result = await _backendSelector.TryMountAsync(request, cancellationToken).ConfigureAwait(false);

				switch (result)
				{
					case ArchiveMountResult.Success success:
						mount = success.Mount;
						break;
					case ArchiveMountResult.CredentialRequired required:
						if (_credentialResolver is null)
						{
							throw new ArchiveCredentialRequiredException(required.Challenge);
						}

						credentialPromptCount++;
						if (credentialPromptCount > _maximumCredentialAttempts || required.Challenge.Attempt > _maximumCredentialAttempts)
						{
							throw new ArchiveOpenException($"Archive credential attempts exceeded {_maximumCredentialAttempts}.");
						}

						credential = await _credentialResolver.ResolveAsync(required.Challenge, cancellationToken).ConfigureAwait(false)
							?? throw new OperationCanceledException("The archive credential request was canceled.");
						credentialAttempt = Math.Max(required.Challenge.Attempt, credentialPromptCount);
						continue;
					case ArchiveMountResult.Unsupported:
						throw new UnsupportedArchiveException(archiveModel.Name);
					case ArchiveMountResult.Failed failed:
						throw new ArchiveOpenException($"Archive '{archiveModel.Name}' could not be opened.", failed.Error);
					default:
						throw new InvalidOperationException("The archive backend selector returned an unknown result.");
				}

				break;
			}

			var locationCoreModel = await mount.ResolveAsync(archiveLocation.EntryPath, cancellationToken).ConfigureAwait(false);
			if (locationCoreModel is not IFolder)
			{
				throw new InvalidOperationException($"Archive entry '{archiveLocation.EntryPath}' is not a folder.");
			}

			locationModel = ReferenceEquals(locationCoreModel, archiveModel.GetCoreModel())
				? archiveModel
				: _modelFactory.Create(mount.ItemSource, locationCoreModel);
			if (locationModel is not IFolderModel folderModel)
			{
				throw new InvalidOperationException($"Archive entry '{archiveLocation.EntryPath}' did not produce a folder model.");
			}

			var context = new ArchiveBrowseLocationContext(archiveLocation, archiveModel, folderModel, mount, _workspace, _modelFactory);

			return context;
		}
		catch (Exception openError)
		{
			var cleanupErrors = new List<Exception>();
			if (locationModel is not null && !ReferenceEquals(locationModel, archiveModel))
			{
				await TryDisposeAsync(locationModel, cleanupErrors).ConfigureAwait(false);
			}

			if (mount is not null)
			{
				await TryDisposeAsync(mount, cleanupErrors).ConfigureAwait(false);
			}

			await TryDisposeAsync(archiveModel, cleanupErrors).ConfigureAwait(false);
			if (cleanupErrors.Count is 0)
			{
				throw;
			}

			cleanupErrors.Insert(0, openError);
			throw new AggregateException("Archive location opening and cleanup failed.", cleanupErrors);
		}
	}

	private IStorageSource GetSource(StorageSourceId sourceId)
	{
		var matches = _workspace.Sources.Where(source => source.SourceId == sourceId).Take(2).ToArray();

		return matches.Length switch
		{
			1 => matches[0],
			0 => throw new KeyNotFoundException($"Storage source '{sourceId}' is not registered."),
			_ => throw new InvalidOperationException($"Storage source '{sourceId}' is registered more than once."),
		};
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
}

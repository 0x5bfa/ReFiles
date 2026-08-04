// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.Models;
using Files.Core.Storage.Windows;
using global::SevenZip;
using OwlCore.Storage;

namespace Files.Core.Storage.Archives.SevenZip;

/// <summary>
/// Provides the Windows 10 and encrypted-archive fallback through SevenZipSharp.
/// </summary>
public sealed class SevenZipArchiveBackend
	: IArchiveBackend, IArchiveProbe
{
	public const string DefaultBackendId = "sevenzip";

	public string Id => DefaultBackendId;

	public int Priority => 100;

	public bool SupportsEncryptedArchives => true;

	public async ValueTask<ArchiveProbeResult> ProbeAsync(ArchiveMountRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		// A probe is only needed before a Shell folder is selected. Other
		// items reach this backend directly, where mounting performs the same
		// password and encryption checks without parsing the archive twice.
		if (request.Source is not WindowsStorageSource || request.ArchiveModel.GetCoreModel() is not IFolder)
		{
			return ArchiveProbeResult.Unknown;
		}

		Stream? stream = null;
		SevenZipExtractor? extractor = null;
		try
		{
			stream = await ArchiveStreamResolver.OpenSeekableReadAsync(request, cancellationToken).ConfigureAwait(false);
			if (stream is null)
			{
				return ArchiveProbeResult.Unknown;
			}

			extractor = CreateExtractor(stream, request.Credential);
			var entries = extractor.ArchiveFileData;
			var encrypted = entries.Any(IsEncrypted);

			if (encrypted && request.Credential is null)
			{
				return ArchiveProbeResult.CredentialRequired(CreateChallenge(request, previousCredentialRejected: false));
			}

			return encrypted
				? ArchiveProbeResult.Encrypted
				: ArchiveProbeResult.Unencrypted;
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception error)
			when (IsPasswordFailure(error))
		{
			return ArchiveProbeResult.CredentialRequired(CreateChallenge(request, previousCredentialRejected: request.Credential is not null));
		}
		catch
		{
			return ArchiveProbeResult.Unknown;
		}
		finally
		{
			extractor?.Dispose();
			if (stream is not null)
			{
				await stream.DisposeAsync().ConfigureAwait(false);
			}
		}
	}

	public async ValueTask<ArchiveMountResult> TryMountAsync(ArchiveMountRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		Stream? stream = null;
		SevenZipExtractor? extractor = null;
		try
		{
			stream = await ArchiveStreamResolver.OpenSeekableReadAsync(request, cancellationToken).ConfigureAwait(false);
			if (stream is null)
			{
				return ArchiveMountResult.Unsupported.Instance;
			}

			extractor = CreateExtractor(stream, request.Credential);
			var entries = extractor.ArchiveFileData.ToArray();
			if (entries.Any(IsEncrypted) && request.Credential is null)
			{
				return new ArchiveMountResult.CredentialRequired(CreateChallenge(request, previousCredentialRejected: false));
			}

			var index = SevenZipArchiveIndex.Create(entries, request.ArchiveModel.Name);
			var mount = new SevenZipArchiveMount(request, stream, extractor, index);
			stream = null;
			extractor = null;

			return new ArchiveMountResult.Success(mount);
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception error)
			when (IsPasswordFailure(error))
		{
			return new ArchiveMountResult.CredentialRequired(CreateChallenge(request, previousCredentialRejected: request.Credential is not null));
		}
		catch (SevenZipOpenFailedException)
		{
			return ArchiveMountResult.Unsupported.Instance;
		}
		catch (Exception error)
		{
			return new ArchiveMountResult.Failed(error);
		}
		finally
		{
			extractor?.Dispose();
			if (stream is not null)
			{
				await stream.DisposeAsync().ConfigureAwait(false);
			}
		}
	}

	internal static bool IsPasswordFailure(Exception error)
	{
		return error is SevenZipOpenFailedException
			{
				Result: OperationResult.WrongPassword,
			}
			|| error is ExtractionFailedException
			{
				Result: OperationResult.WrongPassword,
			};
	}

	internal static SevenZipExtractor CreateExtractor(Stream stream, ArchiveCredential? credential)
	{
		return credential is null
			? new SevenZipExtractor(stream, leaveOpen: true)
			: new SevenZipExtractor(stream, credential.Password, leaveOpen: true);
	}

	private static bool IsEncrypted(ArchiveFileInfo entry)
	{
		return entry.Encrypted
			|| entry.Method?.Contains("Crypto", StringComparison.OrdinalIgnoreCase) is true
			|| entry.Method?.Contains("AES", StringComparison.OrdinalIgnoreCase) is true;
	}

	private static ArchiveCredentialChallenge CreateChallenge(ArchiveMountRequest request, bool previousCredentialRejected)
	{
		return new ArchiveCredentialChallenge(request.Archive, request.ArchiveModel.Name, request.CredentialAttempt + 1, previousCredentialRejected);
	}
}

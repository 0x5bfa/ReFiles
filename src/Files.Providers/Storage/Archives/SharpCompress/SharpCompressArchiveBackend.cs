// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using System.IO;
using global::SharpCompress.Archives;
using global::SharpCompress.Common;
using global::SharpCompress.Readers;

namespace Files.Core.Storage.Archives.SharpCompress;

/// <summary>Reads archive records through SharpCompress without exposing its types to application models.</summary>
public sealed class SharpCompressArchiveBackend : IArchiveBackend
{
	/// <summary>Gets the reader identifier.</summary>
	public const string DefaultBackendId = "sharpcompress";

	/// <inheritdoc />
	public string Id => DefaultBackendId;

	/// <inheritdoc />
	public int Priority => 100;

	/// <inheritdoc />
	public async ValueTask<ArchiveOpenResult> TryOpenAsync(ArchiveOpenRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		return await Task.Run(() => Open(request, cancellationToken), cancellationToken).ConfigureAwait(false);
	}

	internal static bool IsPasswordFailure(Exception error)
	{
		return error is CryptographicException;
	}

	internal static bool IsCredentialRetryCandidate(bool encrypted, Exception error)
	{
		return IsPasswordFailure(error) || encrypted && error is InvalidDataException;
	}

	private static ArchiveOpenResult Open(ArchiveOpenRequest request, CancellationToken cancellationToken)
	{
		IArchive? archive = null;
		try
		{
			cancellationToken.ThrowIfCancellationRequested();

			var options = ReaderOptions.ForExternalStream with { Password = request.Credential?.Password, LeaveStreamOpen = true };
			if (!ArchiveFactory.IsArchive(request.Input, options, out _))
			{
				return ArchiveOpenResult.Unsupported.Instance;
			}

			request.Input.Position = 0;
			archive = ArchiveFactory.OpenArchive(request.Input, options);
			cancellationToken.ThrowIfCancellationRequested();

			var entries = archive.Entries.ToArray();
			if (request.Credential is null && entries.Any(static entry => entry.IsEncrypted))
			{
				return RequiredCredential(request);
			}

			var session = new SharpCompressArchiveReadSession(archive, entries, request.Archive, request.Name, request.CredentialAttempt);
			archive = null;

			return new ArchiveOpenResult.Success(session);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception error) when (IsPasswordFailure(error))
		{
			return RequiredCredential(request);
		}
		catch (Exception error)
		{
			return new ArchiveOpenResult.Failed(error);
		}
		finally
		{
			archive?.Dispose();
		}
	}

	private static ArchiveOpenResult RequiredCredential(ArchiveOpenRequest request)
	{
		return new ArchiveOpenResult.CredentialRequired(new ArchiveCredentialChallenge(request.Archive, request.Name, request.CredentialAttempt + 1, request.CredentialAttempt != 0));
	}
}

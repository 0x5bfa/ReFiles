// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using global::SevenZip;

namespace Files.Core.Storage.Archives.SevenZip;

/// <summary>Reads archive records through SevenZipSharp without exposing its types to application models.</summary>
public sealed class SevenZipArchiveBackend : IArchiveBackend
{
	/// <summary>Gets the reader identifier.</summary>
	public const string DefaultBackendId = "sevenzip";
	private static readonly Lazy<bool> _library = new(InitializeLibrary);

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
		return error is SevenZipOpenFailedException { Result: OperationResult.WrongPassword } or ExtractionFailedException { Result: OperationResult.WrongPassword };
	}

	private static ArchiveOpenResult Open(ArchiveOpenRequest request, CancellationToken cancellationToken)
	{
		SevenZipExtractor? extractor = null;
		try
		{
			_ = _library.Value;
			extractor = request.Credential is null ? new SevenZipExtractor(request.Input, leaveOpen: true) : new SevenZipExtractor(request.Input, request.Credential.Password, leaveOpen: true);
			var records = extractor.ArchiveFileData;
			cancellationToken.ThrowIfCancellationRequested();

			if (request.Credential is null && records.Any(entry => entry.Encrypted || entry.Method?.Contains("AES", StringComparison.OrdinalIgnoreCase) is true))
			{
				return RequiredCredential(request);
			}

			var entries = records.Select(entry => new ArchiveEntryInfo(entry.Index.ToString(CultureInfo.InvariantCulture), GetPath(entry, request.Name), entry.IsDirectory, entry.Size)).ToArray();
			var session = new SevenZipArchiveReadSession(extractor, entries, request.Archive, request.Name);
			extractor = null;

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
			extractor?.Dispose();
		}
	}

	private static string GetPath(ArchiveFileInfo entry, string name)
	{
		return string.IsNullOrEmpty(entry.FileName) || entry.FileName == "[no name]" ? System.IO.Path.GetFileNameWithoutExtension(name) : entry.FileName;
	}

	private static ArchiveOpenResult RequiredCredential(ArchiveOpenRequest request)
	{
		return new ArchiveOpenResult.CredentialRequired(new ArchiveCredentialChallenge(request.Archive, request.Name, request.CredentialAttempt + 1, request.CredentialAttempt != 0));
	}

	private static bool InitializeLibrary()
	{
		var fileName = RuntimeInformation.ProcessArchitecture switch
		{
			Architecture.X64 => "7z64.dll",
			Architecture.Arm64 => "7zArm64.dll",
			_ => throw new PlatformNotSupportedException("The archive reader supports x64 and ARM64 processes."),
		};
		SevenZipBase.SetLibraryPath(Path.Combine(AppContext.BaseDirectory, fileName));

		return true;
	}
}

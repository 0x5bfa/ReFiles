// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using System.IO;
using global::SevenZip;

namespace Files.Core.Storage.Archives.SevenZip;

internal sealed class SevenZipArchiveReadSession : IArchiveReadSession
{
	private readonly SevenZipExtractor _extractor;
	private readonly IReadOnlySet<string> _encryptedRecords;
	private readonly StorableReference _archive;
	private readonly string _name;

	public string BackendId => SevenZipArchiveBackend.DefaultBackendId;

	public IReadOnlyList<ArchiveEntryInfo> Entries { get; }

	public SevenZipArchiveReadSession(SevenZipExtractor extractor, ArchiveEntryInfo[] entries, IReadOnlySet<string> encryptedRecords, StorableReference archive, string name)
	{
		_extractor = extractor;
		_encryptedRecords = encryptedRecords;
		_archive = archive;
		_name = name;
		Entries = Array.AsReadOnly(entries);
	}

	public async Task<Stream> OpenReadAsync(string entryId, CancellationToken cancellationToken = default)
	{
		if (!Entries.Any(entry => entry.Id == entryId && !entry.IsDirectory))
		{
			throw new FileNotFoundException($"Archive record '{entryId}' was not found.");
		}

		var output = ArchiveStreamResolver.CreateTemporaryStream();
		try
		{
			cancellationToken.ThrowIfCancellationRequested();

			using var cancellableOutput = new ExtractionOutput(output, cancellationToken);
			await _extractor.ExtractFileAsync(int.Parse(entryId, CultureInfo.InvariantCulture), cancellableOutput).ConfigureAwait(false);
			cancellationToken.ThrowIfCancellationRequested();

			output.Position = 0;

			return output;
		}
		catch (Exception) when (cancellationToken.IsCancellationRequested)
		{
			await output.DisposeAsync().ConfigureAwait(false);
			throw new OperationCanceledException(cancellationToken);
		}
		catch (Exception error) when (SevenZipArchiveBackend.IsCredentialRetryCandidate(_encryptedRecords.Contains(entryId), error))
		{
			await output.DisposeAsync().ConfigureAwait(false);
			throw new ArchiveCredentialRequiredException(new ArchiveCredentialChallenge(_archive, _name, 1, SevenZipArchiveBackend.IsPasswordFailure(error)), error);
		}
		catch (OperationCanceledException)
		{
			await output.DisposeAsync().ConfigureAwait(false);
			throw;
		}
		catch (Exception error)
		{
			await output.DisposeAsync().ConfigureAwait(false);
			throw new ArchiveOpenException($"Archive record '{entryId}' could not be read and verified.", error);
		}
	}

	public ValueTask DisposeAsync()
	{
		_extractor.Dispose();

		return ValueTask.CompletedTask;
	}

	private sealed class ExtractionOutput(Stream output, CancellationToken cancellationToken) : Stream
	{
		public override bool CanRead => false;
		public override bool CanSeek => output.CanSeek;
		public override bool CanWrite => output.CanWrite;
		public override long Length => output.Length;
		public override long Position { get => output.Position; set => output.Position = value; }

		public override void Flush() => output.Flush();
		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => output.Seek(offset, origin);
		public override void SetLength(long value) => output.SetLength(value);

		public override void Write(byte[] buffer, int offset, int count)
		{
			cancellationToken.ThrowIfCancellationRequested();

			output.Write(buffer, offset, count);
		}
	}
}

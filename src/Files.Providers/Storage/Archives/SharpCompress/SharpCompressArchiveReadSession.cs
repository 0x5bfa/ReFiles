// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using System.IO;
using global::SharpCompress.Archives;
using SharpCompressArchiveEntry = global::SharpCompress.Archives.IArchiveEntry;

namespace Files.Core.Storage.Archives.SharpCompress;

internal sealed class SharpCompressArchiveReadSession : IArchiveReadSession
{
	private readonly IArchive _archive;
	private readonly SharpCompressArchiveEntry[] _entries;
	private readonly StorableReference _reference;
	private readonly string _name;
	private readonly int _credentialAttempt;
	private readonly SemaphoreSlim _gate = new(1, 1);

	public string BackendId => SharpCompressArchiveBackend.DefaultBackendId;

	public IReadOnlyList<ArchiveEntryInfo> Entries { get; }

	public SharpCompressArchiveReadSession(IArchive archive, SharpCompressArchiveEntry[] entries, StorableReference reference, string name, int credentialAttempt)
	{
		_archive = archive;
		_entries = entries;
		_reference = reference;
		_name = name;
		_credentialAttempt = credentialAttempt;
		Entries = Array.AsReadOnly(entries.Select((entry, index) => new ArchiveEntryInfo(index.ToString(CultureInfo.InvariantCulture), GetPath(entry, name), entry.IsDirectory, entry.Size < 0 ? 0 : (ulong)entry.Size)).ToArray());
	}

	public async Task<Stream> OpenReadAsync(string entryId, CancellationToken cancellationToken = default)
	{
		if (!int.TryParse(entryId, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index < 0 || index >= _entries.Length || _entries[index].IsDirectory)
		{
			throw new FileNotFoundException($"Archive record '{entryId}' was not found.");
		}

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		FileStream? output = null;
		try
		{
			output = ArchiveInputStreams.CreateTemporaryStream();
			cancellationToken.ThrowIfCancellationRequested();

			await _entries[index].WriteToAsync(output, new global::SharpCompress.Common.ExtractionOptions { CheckCrc = true }, cancellationToken: cancellationToken).ConfigureAwait(false);

			cancellationToken.ThrowIfCancellationRequested();
			output.Position = 0;
			var result = output;
			output = null;

			return result;
		}
		catch (Exception) when (cancellationToken.IsCancellationRequested)
		{
			if (output is not null)
			{
				await output.DisposeAsync().ConfigureAwait(false);
			}

			throw new OperationCanceledException(cancellationToken);
		}
		catch (Exception error) when (SharpCompressArchiveBackend.IsCredentialRetryCandidate(_entries[index].IsEncrypted, error))
		{
			if (output is not null)
			{
				await output.DisposeAsync().ConfigureAwait(false);
			}

			throw new ArchiveCredentialRequiredException(new ArchiveCredentialChallenge(_reference, _name, _credentialAttempt + 1, _credentialAttempt != 0), error);
		}
		catch (OperationCanceledException)
		{
			if (output is not null)
			{
				await output.DisposeAsync().ConfigureAwait(false);
			}

			throw;
		}
		catch (Exception error)
		{
			if (output is not null)
			{
				await output.DisposeAsync().ConfigureAwait(false);
			}

			throw new ArchiveOpenException($"Archive record '{entryId}' could not be read and verified.", error);
		}
		finally
		{
			_gate.Release();
		}
	}

	public ValueTask DisposeAsync()
	{
		_archive.Dispose();
		_gate.Dispose();

		return ValueTask.CompletedTask;
	}

	private static string GetPath(SharpCompressArchiveEntry entry, string name)
	{
		return string.IsNullOrEmpty(entry.Key) ? Path.GetFileNameWithoutExtension(name) : entry.Key;
	}
}

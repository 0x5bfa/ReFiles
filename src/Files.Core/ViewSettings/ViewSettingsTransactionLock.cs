// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Files.Core.ViewSettings;

internal sealed class ViewSettingsTransactionLock : IAsyncDisposable
{
	private const int RetryDelayMilliseconds = 25;
	private const string LockDirectoryName = "ReFiles.ViewSettings";

	private readonly FileStream? _stream;

	private ViewSettingsTransactionLock(FileStream? stream)
	{
		_stream = stream;
	}

	public async ValueTask DisposeAsync()
	{
		if (_stream is not null)
		{
			await _stream.DisposeAsync().ConfigureAwait(false);
		}
	}

	internal static async ValueTask<ViewSettingsTransactionLock> AcquireAsync(ViewSettingsScopeKey? scope, CancellationToken cancellationToken)
	{
		if (scope is null)
		{
			return new ViewSettingsTransactionLock(null);
		}

		var directoryPath = Path.Combine(Path.GetTempPath(), LockDirectoryName);
		Directory.CreateDirectory(directoryPath);
		var identityBytes = Encoding.UTF8.GetBytes(scope.Value);
		var lockFileName = $"{Convert.ToHexString(SHA256.HashData(identityBytes))}.lock";
		var lockFilePath = Path.Combine(directoryPath, lockFileName);
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();

			try
			{
				var stream = new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1, FileOptions.Asynchronous);

				return new ViewSettingsTransactionLock(stream);
			}
			catch (IOException exception) when ((exception.HResult & 0xFFFF) is 32 or 33)
			{
				await Task.Delay(RetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
			}
		}
	}
}

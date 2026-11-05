// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Files.Core.Settings;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;

namespace Files.Core.Storage.Windows;

/// <summary>Persists settings in a fixed application directory using atomic file replacement.</summary>
public sealed class WindowsSettingsStorage : ISettingsStorage
{
	private readonly string _directory;

	/// <summary>Configures the application's settings directory.</summary>
	/// <param name="localFolderPath">The application's local-state directory.</param>
	public WindowsSettingsStorage(string localFolderPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(localFolderPath);

		_directory = Path.Combine(Path.GetFullPath(localFolderPath), "Settings");
	}

	/// <inheritdoc />
	public async Task<string?> ReadTextAsync(string fileName, CancellationToken cancellationToken = default)
	{
		var path = GetPath(fileName);
		try
		{
			return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
		}
		catch (FileNotFoundException)
		{
			return null;
		}
		catch (DirectoryNotFoundException)
		{
			return null;
		}
	}

	/// <inheritdoc />
	public async Task WriteTextAsync(string fileName, string text, CancellationToken cancellationToken = default)
	{
		var destination = GetPath(fileName);
		ArgumentNullException.ThrowIfNull(text);
		cancellationToken.ThrowIfCancellationRequested();
		Directory.CreateDirectory(_directory);
		var temporary = destination + $".{Guid.NewGuid():N}.tmp";
		try
		{
			await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
			{
				await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true);
				await writer.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
				await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
				stream.Flush(flushToDisk: true);
			}

			cancellationToken.ThrowIfCancellationRequested();
			if (!File.Exists(destination))
			{
				try
				{
					File.Move(temporary, destination);

					return;
				}
				catch (IOException) when (File.Exists(destination))
				{
					// Another writer created the destination before our first commit.
				}
			}

			if (!PInvoke.ReplaceFile(destination, temporary, null, REPLACE_FILE_FLAGS.REPLACEFILE_WRITE_THROUGH))
			{
				var error = Marshal.GetLastPInvokeError();

				throw new Win32Exception(error, "The settings document could not be replaced.");
			}
		}
		finally
		{
			try
			{
				File.Delete(temporary);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				Debug.WriteLine($"Failed to delete temporary settings file: {exception}");
			}
		}
	}

	private string GetPath(string fileName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
		if (fileName != Path.GetFileName(fileName) || fileName is "." or ".." || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
		{
			throw new ArgumentException("Settings documents require a filename without path components.", nameof(fileName));
		}

		return Path.Combine(_directory, fileName);
	}
}

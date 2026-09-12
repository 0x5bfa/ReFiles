// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.IO;

namespace Files.Core.Storage;

/// <summary>
/// Represents an attempt to create an item that already exists.
/// </summary>
public class FileAlreadyExistsException : IOException
{
	private const int FileAlreadyExistsHResult = unchecked((int)0x80070050);

	/// <summary>
	/// Initializes a new instance of the exception.
	/// </summary>
	/// <param name="fileName">The name of the existing item.</param>
	public FileAlreadyExistsException(string fileName)
		: base($"(HRESULT:0x{FileAlreadyExistsHResult:X8}) The file {fileName} already exists.")
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

		HResult = FileAlreadyExistsHResult;
	}
}

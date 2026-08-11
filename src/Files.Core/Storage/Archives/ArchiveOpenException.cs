// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

/// <summary>Indicates that an archive could not be opened.</summary>
public sealed class ArchiveOpenException : Exception
{
	/// <summary>Initializes an archive open exception.</summary>
	/// <param name="message">The exception message.</param>
	/// <param name="innerException">The underlying error.</param>
	public ArchiveOpenException(string message, Exception? innerException = null)
		: base(message, innerException)
	{
	}
}

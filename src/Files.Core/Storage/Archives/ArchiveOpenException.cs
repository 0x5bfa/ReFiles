// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

public sealed class ArchiveOpenException : Exception
{
	public ArchiveOpenException(string message, Exception? innerException = null)
		: base(message, innerException)
	{
	}
}

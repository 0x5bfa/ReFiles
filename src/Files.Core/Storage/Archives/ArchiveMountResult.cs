// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

public abstract record ArchiveMountResult
{
	private ArchiveMountResult()
	{
	}

	public sealed record Success : ArchiveMountResult
	{
		public Success(IArchiveMount mount)
		{
			ArgumentNullException.ThrowIfNull(mount);
			Mount = mount;
		}

		public IArchiveMount Mount { get; }
	}

	public sealed record CredentialRequired : ArchiveMountResult
	{
		public CredentialRequired(ArchiveCredentialChallenge challenge)
		{
			ArgumentNullException.ThrowIfNull(challenge);
			Challenge = challenge;
		}

		public ArchiveCredentialChallenge Challenge { get; }
	}

	public sealed record Unsupported : ArchiveMountResult
	{
		public static Unsupported Instance { get; } = new();

		private Unsupported()
		{
		}
	}

	public sealed record Failed : ArchiveMountResult
	{
		public Failed(Exception error)
		{
			ArgumentNullException.ThrowIfNull(error);
			Error = error;
		}

		public Exception Error { get; }
	}
}

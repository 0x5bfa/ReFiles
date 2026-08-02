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
		public IArchiveMount Mount { get; }

		public Success(IArchiveMount mount)
		{
			ArgumentNullException.ThrowIfNull(mount);

			Mount = mount;
		}
	}

	public sealed record CredentialRequired : ArchiveMountResult
	{
		public ArchiveCredentialChallenge Challenge { get; }

		public CredentialRequired(ArchiveCredentialChallenge challenge)
		{
			ArgumentNullException.ThrowIfNull(challenge);

			Challenge = challenge;
		}
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
		public Exception Error { get; }

		public Failed(Exception error)
		{
			ArgumentNullException.ThrowIfNull(error);

			Error = error;
		}
	}
}

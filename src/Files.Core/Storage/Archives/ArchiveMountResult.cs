// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

/// <summary>Represents the outcome of an archive mount attempt.</summary>
public abstract record ArchiveMountResult
{
	private ArchiveMountResult()
	{
	}

	/// <summary>Indicates that the archive was mounted successfully.</summary>
	public sealed record Success : ArchiveMountResult
	{
		/// <summary>Gets the mounted archive.</summary>
		public IArchiveMount Mount { get; }

		/// <summary>Initializes a successful mount result.</summary>
		/// <param name="mount">The mounted archive.</param>
		public Success(IArchiveMount mount)
		{
			ArgumentNullException.ThrowIfNull(mount);

			Mount = mount;
		}
	}

	/// <summary>Indicates that credentials are required before mounting can continue.</summary>
	public sealed record CredentialRequired : ArchiveMountResult
	{
		/// <summary>Gets the credential challenge.</summary>
		public ArchiveCredentialChallenge Challenge { get; }

		/// <summary>Initializes a credential-required result.</summary>
		/// <param name="challenge">The credential challenge.</param>
		public CredentialRequired(ArchiveCredentialChallenge challenge)
		{
			ArgumentNullException.ThrowIfNull(challenge);

			Challenge = challenge;
		}
	}

	/// <summary>Indicates that no archive backend supports the requested archive.</summary>
	public sealed record Unsupported : ArchiveMountResult
	{
		/// <summary>Gets the shared unsupported result.</summary>
		public static Unsupported Instance { get; } = new();

		private Unsupported()
		{
		}
	}

	/// <summary>Indicates that mounting failed with an error.</summary>
	public sealed record Failed : ArchiveMountResult
	{
		/// <summary>Gets the error that prevented mounting.</summary>
		public Exception Error { get; }

		/// <summary>Initializes a failed mount result.</summary>
		/// <param name="error">The mount error.</param>
		public Failed(Exception error)
		{
			ArgumentNullException.ThrowIfNull(error);

			Error = error;
		}
	}
}

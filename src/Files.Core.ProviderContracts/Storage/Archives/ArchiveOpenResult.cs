// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

/// <summary>Represents the outcome of an archive open attempt.</summary>
public abstract record ArchiveOpenResult
{
	private ArchiveOpenResult()
	{
	}

	/// <summary>Indicates that the archive was opened successfully.</summary>
	public sealed record Success : ArchiveOpenResult
	{
		/// <summary>Gets the opened archive.</summary>
		public IArchiveReadSession Session { get; }

		/// <summary>Initializes a successful open result.</summary>
		/// <param name="session">The opened archive.</param>
		public Success(IArchiveReadSession session)
		{
			ArgumentNullException.ThrowIfNull(session);

			Session = session;
		}
	}

	/// <summary>Indicates that credentials are required before opening can continue.</summary>
	public sealed record CredentialRequired : ArchiveOpenResult
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
	public sealed record Unsupported : ArchiveOpenResult
	{
		/// <summary>Gets the shared unsupported result.</summary>
		public static Unsupported Instance { get; } = new();

		private Unsupported()
		{
		}
	}

	/// <summary>Indicates that opening failed with an error.</summary>
	public sealed record Failed : ArchiveOpenResult
	{
		/// <summary>Gets the error that prevented opening.</summary>
		public Exception Error { get; }

		/// <summary>Initializes a failed open result.</summary>
		/// <param name="error">The open error.</param>
		public Failed(Exception error)
		{
			ArgumentNullException.ThrowIfNull(error);

			Error = error;
		}
	}
}

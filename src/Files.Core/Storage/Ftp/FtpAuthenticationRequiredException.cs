// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Indicates that an FTP source needs new credentials.
/// </summary>
public sealed class FtpAuthenticationRequiredException :
	UnauthorizedAccessException
{
	/// <summary>Gets the connection identifier requiring authentication.</summary>
	public string ConnectionId { get; }

	/// <summary>Initializes an FTP authentication exception.</summary>
	/// <param name="connectionId">The connection identifier.</param>
	/// <param name="message">The exception message.</param>
	/// <param name="innerException">The underlying error.</param>
	public FtpAuthenticationRequiredException(string connectionId, string message, Exception? innerException = null)
		: base(message, innerException)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

		ConnectionId = connectionId;
	}
}

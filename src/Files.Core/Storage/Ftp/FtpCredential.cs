// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Contains transient credentials for one FTP connection attempt.
/// </summary>
public sealed class FtpCredential
{
	/// <summary>Gets the user name.</summary>
	public string UserName { get; }

	/// <summary>Gets the password.</summary>
	public string Password { get; }

	/// <summary>Initializes FTP credentials.</summary>
	/// <param name="userName">The user name.</param>
	/// <param name="password">The password.</param>
	public FtpCredential(string userName, string password)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(userName);
		ArgumentNullException.ThrowIfNull(password);

		UserName = userName;
		Password = password;
	}

	/// <summary>Returns a redacted credential description.</summary>
	/// <returns>The user name and a redacted password marker.</returns>
	public override string ToString() => $"{UserName}:***";
}

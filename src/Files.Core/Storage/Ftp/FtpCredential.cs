// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Contains transient credentials for one FTP connection attempt.
/// </summary>
public sealed class FtpCredential
{
	public string UserName { get; }

	public string Password { get; }

	public FtpCredential(string userName, string password)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(userName);
		ArgumentNullException.ThrowIfNull(password);

		UserName = userName;
		Password = password;
	}

	public override string ToString() => $"{UserName}:***";
}

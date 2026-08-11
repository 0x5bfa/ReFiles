// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Describes how one FTP server compares remote paths.
/// </summary>
public enum FtpPathComparison
{
	/// <summary>Compare path segments case-sensitively.</summary>
	CaseSensitive,
	/// <summary>Compare path segments case-insensitively.</summary>
	CaseInsensitive,
}

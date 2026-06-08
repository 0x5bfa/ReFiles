// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Selects the transport security used by an FTP connection.
/// </summary>
public enum FtpSecurityMode
{
	Plain,
	ExplicitTls,
	ImplicitTls,
}

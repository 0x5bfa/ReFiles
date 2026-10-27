// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.BackgroundServer;

/// <summary>Registers non-secret connection settings independently of transfer intent.</summary>
public struct FtpConnectionData
{
	public string ConnectionId;
	public string DisplayName;
	public string Host;
	public int Port;
	public int SecurityMode;
	public string RootPath;
	public string UserNameHint;
	public int PathComparison;
}

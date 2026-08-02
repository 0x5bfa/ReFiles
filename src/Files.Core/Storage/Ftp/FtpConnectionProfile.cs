// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Contains non-secret settings for one configured FTP connection.
/// </summary>
public sealed record FtpConnectionProfile
{
	public string ConnectionId { get; }

	public string DisplayName { get; }

	public string Host { get; }

	public int Port { get; }

	public FtpSecurityMode SecurityMode { get; }

	public FtpPath RootPath { get; }

	public string? UserNameHint { get; }

	public FtpPathComparison PathComparison { get; }

	internal StringComparer PathComparer =>
		PathComparison is FtpPathComparison.CaseInsensitive
			? StringComparer.OrdinalIgnoreCase
			: StringComparer.Ordinal;

	public FtpConnectionProfile(
		string connectionId,
		string displayName,
		string host,
		int? port = null,
		FtpSecurityMode securityMode = FtpSecurityMode.Plain,
		string rootPath = "/",
		string? userNameHint = null,
		FtpPathComparison pathComparison =
			FtpPathComparison.CaseSensitive)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
		ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
		ArgumentException.ThrowIfNullOrWhiteSpace(host);

		ValidateSecurityMode(securityMode);
		ValidatePathComparison(pathComparison);

		var normalizedHost = NormalizeHost(host);
		var resolvedPort = port ?? GetDefaultPort(securityMode);
		if (resolvedPort is < 1 or > ushort.MaxValue)
		{
			throw new ArgumentOutOfRangeException(nameof(port), "An FTP port must be between 1 and 65535.");
		}

		ConnectionId = connectionId;
		DisplayName = displayName;
		Host = normalizedHost;
		Port = resolvedPort;
		SecurityMode = securityMode;
		RootPath = FtpPath.Parse(rootPath);
		UserNameHint = string.IsNullOrWhiteSpace(userNameHint)
			? null
			: userNameHint;
		PathComparison = pathComparison;
	}

	private static string NormalizeHost(string host)
	{
		var value = host.Trim();
		if (value.Length > 1 && value[0] is '[' && value[^1] is ']')
		{
			value = value[1..^1];
		}

		if (value.Length is 0 || value.Any(char.IsWhiteSpace) || value.Contains('/') || value.Contains('\\') || value.Contains('@'))
		{
			throw new ArgumentException("The FTP host must be a hostname or IP address without a URI scheme.", nameof(host));
		}

		var uriHost = value.Contains(':')
			? $"[{value}]"
			: value;
		if (Uri.CheckHostName(value) is UriHostNameType.Unknown
			|| !Uri.TryCreate(
			$"ftp://{uriHost}/",
			UriKind.Absolute,
			out var endpoint)
			|| string.IsNullOrWhiteSpace(endpoint.IdnHost)
			|| !string.IsNullOrEmpty(endpoint.UserInfo))
		{
			throw new ArgumentException("The FTP host must be a valid hostname or IP address.", nameof(host));
		}

		return value;
	}

	private static int GetDefaultPort(FtpSecurityMode securityMode)
	{
		return securityMode is FtpSecurityMode.ImplicitTls
			? 990
			: 21;
	}

	private static void ValidateSecurityMode(FtpSecurityMode securityMode)
	{
		if (securityMode is not FtpSecurityMode.Plain and not FtpSecurityMode.ExplicitTls and not FtpSecurityMode.ImplicitTls)
		{
			throw new ArgumentOutOfRangeException(nameof(securityMode));
		}
	}

	private static void ValidatePathComparison(FtpPathComparison pathComparison)
	{
		if (pathComparison is not FtpPathComparison.CaseSensitive and not FtpPathComparison.CaseInsensitive)
		{
			throw new ArgumentOutOfRangeException(nameof(pathComparison));
		}
	}
}

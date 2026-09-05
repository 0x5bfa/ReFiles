// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Contains the default NTFS quota policy for a volume.
/// </summary>
public sealed class WindowsShellQuotaProperties
{
	/// <summary>Gets the volume root path.</summary>
	public string RootPath { get; }

	/// <summary>Gets the Shell display name for the volume.</summary>
	public string DisplayName { get; }

	/// <summary>Gets a value indicating whether reading quota policy requires elevation.</summary>
	public bool RequiresElevation { get; }

	/// <summary>Gets a value indicating whether quota tracking is enabled.</summary>
	public bool IsTrackingEnabled { get; }

	/// <summary>Gets a value indicating whether quota limits are enforced.</summary>
	public bool IsLimitEnforced { get; }

	/// <summary>Gets a value indicating whether limit events are logged.</summary>
	public bool LogsLimitEvents { get; }

	/// <summary>Gets a value indicating whether warning events are logged.</summary>
	public bool LogsWarningEvents { get; }

	/// <summary>Gets the default per-user quota limit in bytes, or -1 for no limit.</summary>
	public long DefaultLimit { get; }

	/// <summary>Gets the default per-user warning threshold in bytes, or -1 for no threshold.</summary>
	public long DefaultThreshold { get; }

	internal WindowsShellQuotaProperties(
		string rootPath,
		string displayName,
		bool requiresElevation,
		bool isTrackingEnabled,
		bool isLimitEnforced,
		bool logsLimitEvents,
		bool logsWarningEvents,
		long defaultLimit,
		long defaultThreshold)
	{
		RootPath = rootPath;
		DisplayName = displayName;
		RequiresElevation = requiresElevation;
		IsTrackingEnabled = isTrackingEnabled;
		IsLimitEnforced = isLimitEnforced;
		LogsLimitEvents = logsLimitEvents;
		LogsWarningEvents = logsWarningEvents;
		DefaultLimit = defaultLimit;
		DefaultThreshold = defaultThreshold;
	}
}

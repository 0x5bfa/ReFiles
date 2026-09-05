// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Describes one contextual Windows Shell command without retaining apartment-bound COM objects.
/// </summary>
public sealed class WindowsShellContextualCommand
{
	internal WindowsShellContextualCommandToken Token { get; }

	/// <summary>Gets the stable command identifier.</summary>
	public string Id { get; }

	/// <summary>Gets a value indicating whether the command can currently be invoked.</summary>
	public bool IsEnabled { get; }

	/// <summary>Gets the context whose changes can affect this command.</summary>
	public WindowsShellContextualCommandScope Scope { get; }

	internal WindowsShellContextualCommand(string id, bool isEnabled, WindowsShellContextualCommandScope scope, WindowsShellContextualCommandToken token)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(id);
		ArgumentNullException.ThrowIfNull(token);

		Id = id;
		IsEnabled = isEnabled;
		Scope = scope;
		Token = token;
	}
}

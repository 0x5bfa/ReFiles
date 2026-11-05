// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Windows;

namespace Files.Core;

/// <summary>Owns a UI runtime and exposes its borrowed Windows integrations.</summary>
public interface IUiFilesRuntime : IFilesCoreRuntime
{
	/// <summary>Gets the Windows integrations owned by this runtime.</summary>
	IWindowsShellUiServices Windows { get; }
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Data;
using Files.Core.Storage;
using Files.Core.Windows;

namespace Files.Presentation;

internal sealed class WindowsBrowseInteraction
{
	internal WindowsStorageSource? Source { get; }
	internal WindowsShellPropertiesService? Properties { get; }
	internal WindowsShellContextualCommandService? ContextualCommands { get; }
	internal IWindowsShellOperationService? Operations { get; }

	internal WindowsBrowseInteraction(IStorageWorkspace workspace, IStorageOperationService operations, nint ownerWindowHandle)
	{
		Source = workspace.Sources.OfType<WindowsStorageSource>().FirstOrDefault();
		Properties = Source is { } source ? new WindowsShellPropertiesService(source) : null;
		ContextualCommands = Source is { } contextualSource && ownerWindowHandle != 0 ? new WindowsShellContextualCommandService(contextualSource) : null;
		Operations = operations as IWindowsShellOperationService;
	}
}

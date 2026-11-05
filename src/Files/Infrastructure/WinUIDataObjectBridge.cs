// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.ApplicationModel.DataTransfer;

namespace Files.Infrastructure;

internal static class WinUIDataObjectBridge
{
	internal static DataPackageOperation Attach(IWindowsShellDragSource dragSource, DataPackage dataPackage, nint ownerWindowHandle,
		WindowsShellDropEffects preferredEffect = WindowsShellDropEffects.None, bool deriveMoveFromDelete = true, ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(dragSource);

		ArgumentNullException.ThrowIfNull(dataPackage);

		var effects = dragSource.Attach(dataPackage, ownerWindowHandle, preferredEffect, deriveMoveFromDelete);
		var resolvedLogger = logger ?? NullLogger.Instance;
		if (resolvedLogger.IsEnabled(LogLevel.Debug))
		{
			resolvedLogger.LogDebug("{Component}: {Message}", "ShellDragSource", $"Attach allowed={effects} preferred={preferredEffect}");
		}

		return ToDataPackageOperation(effects);
	}

	internal static IWindowsShellDataTransfer GetDataObject(IWindowsShellDragDropService service, DataPackageView dataPackageView)
	{
		return service.CaptureDataTransfer(dataPackageView);
	}

	internal static DataPackageOperation ToDataPackageOperation(WindowsShellDropEffects effects)
	{
		var operation = DataPackageOperation.None;
		if (effects.HasFlag(WindowsShellDropEffects.Copy))
		{
			operation |= DataPackageOperation.Copy;
		}

		if (effects.HasFlag(WindowsShellDropEffects.Move))
		{
			operation |= DataPackageOperation.Move;
		}

		if (effects.HasFlag(WindowsShellDropEffects.Link))
		{
			operation |= DataPackageOperation.Link;
		}

		return operation;
	}
}

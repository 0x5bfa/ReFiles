// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.ApplicationModel.DataTransfer;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.UI.Shell;
using WinRT;

namespace Files.Infrastructure;

internal static class WinUIDataObjectBridge
{
	internal static DataPackageOperation Attach(WindowsShellDragSource dragSource, DataPackage dataPackage, nint ownerWindowHandle,
		WindowsShellDropEffects preferredEffect = WindowsShellDropEffects.None, bool deriveMoveFromDelete = true, ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(dragSource);

		ArgumentNullException.ThrowIfNull(dataPackage);

		var effects = dragSource.Attach(GetProvider(dataPackage), ownerWindowHandle, preferredEffect, deriveMoveFromDelete);
		var resolvedLogger = logger ?? NullLogger.Instance;
		if (resolvedLogger.IsEnabled(LogLevel.Debug))
		{
			resolvedLogger.LogDebug("{Component}: {Message}", "ShellDragSource", $"Attach allowed={effects} preferred={preferredEffect}");
		}

		return ToDataPackageOperation(effects);
	}

	internal static IDataObjectProvider GetProvider(DataPackage dataPackage)
	{
		ArgumentNullException.ThrowIfNull(dataPackage);

		return dataPackage.As<IDataObjectProvider>();
	}

	internal static IDataObject GetDataObject(DataPackageView dataPackageView)
	{
		ArgumentNullException.ThrowIfNull(dataPackageView);

		return dataPackageView.As<IDataObject>();
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

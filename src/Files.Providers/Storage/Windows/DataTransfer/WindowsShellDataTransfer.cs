// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Windows.ApplicationModel.DataTransfer;
using Windows.Win32.System.Com;
using WinRT;

namespace Files.Core.Storage.Windows;

/// <summary>Retains a Shell data object obtained on the calling STA without exposing its native interface.</summary>
public sealed class WindowsShellDataTransfer : IWindowsShellDataTransfer
{
	internal IDataObject DataObject { get; }

	private WindowsShellDataTransfer(IDataObject dataObject)
	{
		DataObject = dataObject;
	}

	/// <summary>Obtains the native data object on the calling STA, where it must subsequently be used.</summary>
	/// <param name="dataPackageView">The incoming WinRT data view.</param>
	/// <returns>A managed reference retaining the Shell data object.</returns>
	public static WindowsShellDataTransfer FromDataPackageView(DataPackageView dataPackageView)
	{
		ArgumentNullException.ThrowIfNull(dataPackageView);

		return new WindowsShellDataTransfer(dataPackageView.As<IDataObject>());
	}
}

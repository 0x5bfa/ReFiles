// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Storage.Windows;

/// <summary>Represents a file exposed by the Windows Shell.</summary>
public sealed class WindowsFile : WindowsStorable, IFile
{
	internal WindowsFile(WindowsStorableDescriptor descriptor, WindowsStorableFactory factory)
		: base(descriptor, factory)
	{
	}
}

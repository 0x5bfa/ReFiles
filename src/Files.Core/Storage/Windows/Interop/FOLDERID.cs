// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System;

namespace Windows.Win32
{
	/// <summary>Contains manually declared Shell folder identifiers used by Files.</summary>
	public static partial class FOLDERID
	{
		/// <summary>The Recycle Bin folder identifier.</summary>
		public static Guid FOLDERID_RecycleBinFolder { get; } = new(0xB7534046u, 0x3ECB, 0x4C18, 0xBE, 0x4E, 0x64, 0xCD, 0x4C, 0xB7, 0xD6, 0xAC);
		/// <summary>The Computer folder identifier.</summary>
		public static Guid FOLDERID_ComputerFolder { get; } = new(0x0AC0837Cu, 0xBBF8, 0x452A, 0x85, 0x0D, 0x79, 0xD0, 0x8E, 0x66, 0x7C, 0xA7);
	}
}

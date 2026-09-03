// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Runtime.InteropServices;

namespace Windows.Win32
{
	/// <summary>Contains manually declared COM class identifiers used by Files.</summary>
	public static partial class CLSID
	{
		/// <summary>The My Computer Shell class identifier.</summary>
		public static Guid CLSID_MyComputer { get; } = new(0x20D04FE0u, 0x3AEA, 0x1069, 0xA2, 0xD8, 0x08, 0x00, 0x2B, 0x30, 0x30, 0x9D);
		/// <summary>The Pin to Frequent Execute class identifier.</summary>
		public static Guid CLSID_PinToFrequentExecute { get; } = new(0xB455F46Eu, 0xE4AF, 0x4035, 0xB0, 0xA4, 0xCF, 0x18, 0xD2, 0xF6, 0xF2, 0x8E);
		/// <summary>The Unpin from Frequent Execute class identifier.</summary>
		public static Guid CLSID_UnPinFromFrequentExecute { get; } = new(0xEE20EEBAu, 0xDF64, 0x4A4E, 0xB7, 0xBB, 0x2D, 0x1C, 0x6B, 0x2D, 0xFC, 0xC1);
		/// <summary>The New menu class identifier.</summary>
		public static Guid CLSID_NewMenu { get; } = new(0xD969A300u, 0xE7FF, 0x11D0, 0xA9, 0x3B, 0x00, 0xA0, 0xC9, 0x0F, 0x27, 0x19);
		/// <summary>The Open With menu class identifier.</summary>
		public static Guid CLSID_OpenWithMenu { get; } = new(0x09799AFBu, 0xAD67, 0x11D1, 0xAB, 0xCD, 0x00, 0xC0, 0x4F, 0xC3, 0x09, 0x36);
	}

	/// <summary>Contains manually declared Shell folder identifiers used by Files.</summary>
	public static partial class FOLDERID
	{
		/// <summary>The Recycle Bin folder identifier.</summary>
		public static Guid FOLDERID_RecycleBinFolder { get; } = new(0xB7534046u, 0x3ECB, 0x4C18, 0xBE, 0x4E, 0x64, 0xCD, 0x4C, 0xB7, 0xD6, 0xAC);
		/// <summary>The Computer folder identifier.</summary>
		public static Guid FOLDERID_ComputerFolder { get; } = new(0x0AC0837Cu, 0xBBF8, 0x452A, 0x85, 0x0D, 0x79, 0xD0, 0x8E, 0x66, 0x7C, 0xA7);
	}
}

namespace Windows.Win32.UI.Shell
{
	/// <summary>Identifies the NTFS disk-quota controller coclass.</summary>
	[Guid("7988B571-EC89-11CF-9C00-00AA00A14F56")]
	internal sealed class CDiskQuotaControl
	{
	}
}

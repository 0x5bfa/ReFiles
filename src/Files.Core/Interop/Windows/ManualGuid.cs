// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using Windows.Win32.System.WinRT;

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
		/// <summary>The Detection and Sharing class identifier.</summary>
		public static Guid CLSID_DetectionAndSharing { get; } = new(0x1FDA955Bu, 0x61FF, 0x11DA, 0x97, 0x8C, 0x00, 0x08, 0x74, 0x4F, 0xAA, 0xB7);
		/// <summary>The Open Control Panel class identifier.</summary>
		public static Guid CLSID_OpenControlPanel { get; } = new(0x06622D85u, 0x6856, 0x4460, 0x8D, 0xE1, 0xA8, 0x19, 0x21, 0xB4, 0x1C, 0x4B);

		/// <summary>The NTFS disk-quota controller class identifier.</summary>
		public static Guid CLSID_DiskQuotaControl { get; } = new(0x7988B571u, 0xEC89, 0x11CF, 0x9C, 0x00, 0x00, 0xAA, 0x00, 0xA1, 0x4F, 0x56);

		/// <summary>The NTFS security Shell extension class identifier.</summary>
		public static Guid CLSID_NTFSSecurityExt { get; } = new(0x1F2E5C40u, 0x9550, 0x11CE, 0x99, 0xD2, 0x00, 0xAA, 0x00, 0x6E, 0x08, 0x6C);

		/// <summary>The elevated disk-quota UI helper class identifier.</summary>
		public static Guid CLSID_QuotaUIHelper { get; } = new(0x1FB2A002u, 0x4C6C, 0x4DE7, 0x85, 0xC2, 0xCB, 0x8D, 0xB9, 0xA4, 0xF7, 0x28);
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

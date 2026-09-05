// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System;

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
}

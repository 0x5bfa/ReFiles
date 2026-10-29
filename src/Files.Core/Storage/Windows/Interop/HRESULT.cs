// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Windows.Win32.Foundation
{
	[DebuggerDisplay("{" + nameof(Value) + ",h}")]
	public readonly partial struct HRESULT
	{
		/// <summary>
		/// Converts a <see cref="WIN32_ERROR"/> value to <see cref="HRESULT"/>.
		/// </summary>
		/// <param name="error">The Win32 error value to convert.</param>
		/// <returns>The result of the <c>HRESULT_FROM_WIN32</c> conversion.</returns>
		public static HRESULT FromWin32Error(WIN32_ERROR error)
		{
			/*
				#define HRESULT_FROM_WIN32(x) \
				((HRESULT)(x) <= 0 ? (HRESULT)(x) : \
				(HRESULT)(((x) & 0xFFFF) | (7 << 16) | 0x80000000))
			*/
			var value = unchecked((int)error);
			return value <= 0
				? (HRESULT)value
				: (HRESULT)unchecked((int)(0x80070000u | ((uint)error & 0xFFFFu)));
		}
	}
}

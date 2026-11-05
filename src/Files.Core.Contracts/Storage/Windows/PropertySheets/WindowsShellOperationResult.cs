// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;

namespace Files.Core.Storage.Windows;

/// <summary>Preserves a Shell operation's HRESULT without exposing generated native types.</summary>
/// <param name="HResult">The operation's HRESULT value.</param>
public readonly record struct WindowsShellOperationResult(int HResult)
{
	/// <summary>Gets whether the operation returned a successful status, including S_FALSE.</summary>
	public bool Succeeded => HResult >= 0;
	/// <summary>Gets whether the operation failed.</summary>
	public bool Failed => HResult < 0;

	/// <summary>Throws the corresponding exception when the operation failed.</summary>
	public void ThrowOnFailure()
	{
		Marshal.ThrowExceptionForHR(HResult);
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;
using Windows.Win32.System.WinRT;

namespace Windows.Win32;

[CustomMarshaller(typeof(string), MarshalMode.ManagedToUnmanagedIn, typeof(HStringStringMarshaller.ManagedToUnmanagedIn))]
[CustomMarshaller(typeof(string), MarshalMode.ManagedToUnmanagedOut, typeof(HStringStringMarshaller.ManagedToUnmanagedOut))]
[CustomMarshaller(typeof(string), MarshalMode.UnmanagedToManagedIn, typeof(HStringStringMarshaller.UnmanagedToManagedIn))]
[CustomMarshaller(typeof(string), MarshalMode.UnmanagedToManagedOut, typeof(HStringStringMarshaller.UnmanagedToManagedOut))]
[CustomMarshaller(typeof(string), MarshalMode.Default, typeof(HStringStringMarshaller.Stateless))]
internal static unsafe class HStringStringMarshaller
{
	/// <summary>Provides stateless HSTRING conversion for collection elements.</summary>
	public static class Stateless
	{
		/// <summary>Converts a managed string to an owned HSTRING.</summary>
		/// <param name="managed">The managed string.</param>
		/// <returns>The HSTRING.</returns>
		public static HSTRING ConvertToUnmanaged(string? managed)
		{
			return CreateHString(managed);
		}

		/// <summary>Converts an HSTRING to a managed string.</summary>
		/// <param name="unmanaged">The HSTRING.</param>
		/// <returns>The managed string.</returns>
		public static string? ConvertToManaged(HSTRING unmanaged)
		{
			return ToManagedString(unmanaged);
		}

		/// <summary>Releases an owned HSTRING.</summary>
		/// <param name="unmanaged">The HSTRING.</param>
		public static void Free(HSTRING unmanaged)
		{
			DeleteHString(unmanaged);
		}
	}

	/// <summary>Marshals a managed input string to an HSTRING.</summary>
	public ref struct ManagedToUnmanagedIn
	{
		private HSTRING _hstring;

		/// <summary>Initializes the marshaller from a managed string.</summary>
		/// <param name="managed">The managed string.</param>
		public void FromManaged(string? managed)
		{
			_hstring = CreateHString(managed);
		}

		/// <summary>Gets the marshalled HSTRING.</summary>
		/// <returns>The HSTRING.</returns>
		public HSTRING ToUnmanaged()
			=> _hstring;

		/// <summary>Releases the marshalled HSTRING.</summary>
		public void Free()
		{
			DeleteHString(_hstring);
		}
	}

	/// <summary>Marshals a managed output string to an HSTRING for a native caller.</summary>
	public ref struct UnmanagedToManagedOut
	{
		private HSTRING _hstring;

		/// <summary>Initializes the marshaller from a managed string.</summary>
		/// <param name="managed">The managed string.</param>
		public void FromManaged(string? managed)
		{
			_hstring = CreateHString(managed);
		}

		/// <summary>Gets the marshalled HSTRING.</summary>
		/// <returns>The HSTRING.</returns>
		public HSTRING ToUnmanaged()
			=> _hstring;

		/// <summary>Leaves ownership of the HSTRING with the native caller.</summary>
		public void Free()
		{
		}
	}

	/// <summary>Marshals a borrowed HSTRING input to a managed string.</summary>
	public ref struct UnmanagedToManagedIn
	{
		private HSTRING _hstring;

		/// <summary>Initializes the marshaller from an HSTRING.</summary>
		/// <param name="unmanaged">The HSTRING.</param>
		public void FromUnmanaged(HSTRING unmanaged)
		{
			_hstring = unmanaged;
		}

		/// <summary>Gets the managed string.</summary>
		/// <returns>The managed string.</returns>
		public string? ToManaged()
		{
			return ToManagedString(_hstring);
		}

		/// <summary>Leaves ownership of the borrowed HSTRING unchanged.</summary>
		public void Free()
		{
		}
	}

	/// <summary>Marshals an HSTRING output to a managed string.</summary>
	public ref struct ManagedToUnmanagedOut
	{
		private HSTRING _hstring;

		/// <summary>Initializes the marshaller from an HSTRING.</summary>
		/// <param name="unmanaged">The HSTRING.</param>
		public void FromUnmanaged(HSTRING unmanaged)
		{
			_hstring = unmanaged;
		}

		/// <summary>Gets the managed string.</summary>
		/// <returns>The managed string.</returns>
		public string? ToManaged()
		{
			return ToManagedString(_hstring);
		}

		/// <summary>Releases the returned HSTRING.</summary>
		public void Free()
		{
			DeleteHString(_hstring);
		}
	}

	private static HSTRING CreateHString(string? managed)
	{
		if (managed is null)
		{
			return default;
		}

		var hr = PInvoke.WindowsCreateString(managed, checked((uint)managed.Length), out var hstring);
		if (hr.Failed)
		{
			hstring.Dispose();
			Marshal.ThrowExceptionForHR(hr.Value);
		}

		using (hstring)
		{
			return hstring.Detach();
		}
	}

	private static string? ToManagedString(HSTRING hstring)
	{
		if (hstring.IsNull)
		{
			return null;
		}

		using var stringHandle = new WindowsDeleteStringSafeHandle(hstring, false);
		PCWSTR buffer = PInvoke.WindowsGetStringRawBuffer(stringHandle, out var length);

		return new string(buffer.Value, 0, checked((int)length));
	}

	private static void DeleteHString(HSTRING hstring)
	{
		if (!hstring.IsNull)
		{
			PInvoke.WindowsDeleteString(hstring);
		}
	}
}

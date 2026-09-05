// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace Windows.Win32.System.WinRT;

/// <summary>Reads scalar and array values from a Windows Runtime property value.</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("4BD682DD-7554-40E9-9A9B-82654EDE7E62")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IPropertyValue : IInspectable
{
	/// <summary>Gets the stored property type.</summary>
	/// <param name="value">Receives the property type.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetPropertyType(out global::Windows.Foundation.PropertyType value);

	/// <summary>Gets whether the value is a numeric scalar.</summary>
	/// <param name="value">Receives whether the value is a numeric scalar.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetIsNumericScalar([MarshalAs(UnmanagedType.U1)] out bool value);

	/// <summary>Gets an unsigned 8-bit value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetUInt8(out byte value);

	/// <summary>Gets a signed 16-bit value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetInt16(out short value);

	/// <summary>Gets an unsigned 16-bit value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetUInt16(out ushort value);

	/// <summary>Gets a signed 32-bit value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetInt32(out int value);

	/// <summary>Gets an unsigned 32-bit value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetUInt32(out uint value);

	/// <summary>Gets a signed 64-bit value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetInt64(out long value);

	/// <summary>Gets an unsigned 64-bit value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetUInt64(out ulong value);

	/// <summary>Gets a single-precision value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetSingle(out float value);

	/// <summary>Gets a double-precision value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetDouble(out double value);

	/// <summary>Gets a UTF-16 character.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetChar16(out char value);

	/// <summary>Gets a Boolean value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetBoolean([MarshalAs(UnmanagedType.U1)] out bool value);

	/// <summary>Gets a string value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetString([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] out string? value);

	/// <summary>Gets a GUID value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetGuid(out Guid value);

	/// <summary>Gets a Windows Runtime date-time ABI value.</summary>
	/// <param name="universalTime">Receives the universal-time tick value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetDateTime(out long universalTime);

	/// <summary>Gets a Windows Runtime time-span ABI value.</summary>
	/// <param name="duration">Receives the duration tick value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetTimeSpan(out long duration);

	/// <summary>Gets a point value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetPoint(out global::Windows.Foundation.Point value);

	/// <summary>Gets a size value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetSize(out global::Windows.Foundation.Size value);

	/// <summary>Gets a rectangle value.</summary>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetRect(out global::Windows.Foundation.Rect value);

	/// <summary>Gets an unsigned 8-bit array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetUInt8Array(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out byte[]? value);

	/// <summary>Gets a signed 16-bit array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetInt16Array(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out short[]? value);

	/// <summary>Gets an unsigned 16-bit array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetUInt16Array(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out ushort[]? value);

	/// <summary>Gets a signed 32-bit array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetInt32Array(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out int[]? value);

	/// <summary>Gets an unsigned 32-bit array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetUInt32Array(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out uint[]? value);

	/// <summary>Gets a signed 64-bit array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetInt64Array(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out long[]? value);

	/// <summary>Gets an unsigned 64-bit array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetUInt64Array(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out ulong[]? value);

	/// <summary>Gets a single-precision array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetSingleArray(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out float[]? value);

	/// <summary>Gets a double-precision array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetDoubleArray(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out double[]? value);

	/// <summary>Gets a UTF-16 character array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetChar16Array(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out ushort[]? value);

	/// <summary>Gets a Windows Runtime Boolean array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated one-byte Boolean values.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetBooleanArray(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out byte[]? value);

	/// <summary>Gets a string array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetStringArray(out uint valueLength,
		[MarshalUsing(CountElementName = nameof(valueLength))]
		[MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller), ElementIndirectionDepth = 1)] out string?[]? value);

	/// <summary>Gets an inspectable array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetInspectableArray(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out IInspectable?[]? value);

	/// <summary>Gets a GUID array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetGuidArray(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out Guid[]? value);

	/// <summary>Gets a Windows Runtime date-time ABI array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated universal-time tick values.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetDateTimeArray(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out long[]? value);

	/// <summary>Gets a Windows Runtime time-span ABI array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated duration tick values.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetTimeSpanArray(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out long[]? value);

	/// <summary>Gets a point array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetPointArray(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out global::Windows.Foundation.Point[]? value);

	/// <summary>Gets a size array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetSizeArray(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out global::Windows.Foundation.Size[]? value);

	/// <summary>Gets a rectangle array.</summary>
	/// <param name="valueLength">Receives the array length.</param>
	/// <param name="value">Receives the allocated array.</param>
	/// <returns>The HRESULT returned by the value.</returns>
	[PreserveSig]
	HRESULT GetRectArray(out uint valueLength, [MarshalUsing(CountElementName = nameof(valueLength))] out global::Windows.Foundation.Rect[]? value);
}

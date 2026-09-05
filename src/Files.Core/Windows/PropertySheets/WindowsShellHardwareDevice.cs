// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Describes one device displayed by Explorer's drive Hardware page.
/// </summary>
public sealed class WindowsShellHardwareDevice
{
	/// <summary>Gets the PNG data for the device icon loaded by SetupAPI.</summary>
	public ReadOnlyMemory<byte> IconData { get; }

	/// <summary>Gets the device's display name.</summary>
	public string Name { get; }

	/// <summary>Gets the localized setup-class description.</summary>
	public string Type { get; }

	/// <summary>Gets the device manufacturer.</summary>
	public string Manufacturer { get; }

	/// <summary>Gets the device location description.</summary>
	public string Location { get; }

	/// <summary>Gets the device UI location number when one is assigned.</summary>
	public uint? LocationNumber { get; }

	/// <summary>Gets the provider-supplied format for the device UI location number.</summary>
	public string LocationNumberFormat { get; }

	/// <summary>Gets the Configuration Manager status flags.</summary>
	public uint Status { get; }

	/// <summary>Gets the Configuration Manager problem code.</summary>
	public uint ProblemCode { get; }

	/// <summary>Gets the stable device-instance identifier.</summary>
	public string InstanceId { get; }

	internal WindowsShellHardwareDevice(
		ReadOnlyMemory<byte> iconData,
		string name,
		string type,
		string manufacturer,
		string location,
		uint? locationNumber,
		string locationNumberFormat,
		uint status,
		uint problemCode,
		string instanceId)
	{
		IconData = iconData;
		Name = name;
		Type = type;
		Manufacturer = manufacturer;
		Location = location;
		LocationNumber = locationNumber;
		LocationNumberFormat = locationNumberFormat;
		Status = status;
		ProblemCode = problemCode;
		InstanceId = instanceId;
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities;

/// <summary>
/// Describes who owns an item capability created by a factory.
/// </summary>
public enum CapabilityLifetime
{
	/// <summary>
	/// The item owns and disposes the capability.
	/// </summary>
	Item,

	/// <summary>
	/// The factory or another composition root owns the shared capability.
	/// </summary>
	Shared,
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Capabilities.Previews;

/// <summary>Captures provider-specific identity matching without introducing provider types into the runtime.</summary>
public interface IPreviewTrustTarget
{
	/// <summary>Captures immutable identity data for the current target.</summary>
	/// <returns>The captured matcher, or null when only object identity can be used.</returns>
	IPreviewTrustIdentity? CaptureTrustIdentity();
}

/// <summary>Matches a newly resolved raw item against a captured preview target.</summary>
public interface IPreviewTrustIdentity
{
	/// <summary>Checks provider-specific target identity.</summary>
	/// <param name="item">The newly resolved raw item.</param>
	/// <returns>Whether this is the captured target.</returns>
	bool Matches(IStorable item);
}

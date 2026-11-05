// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Browsing;

/// <summary>
/// Resolves a logical parent when it cannot be represented by the current
/// location model's storage parent alone.
/// </summary>
public interface IBrowseLocationParentResolver
{
	/// <summary>Gets a value indicating whether a logical parent can be resolved.</summary>
	bool CanGetParent { get; }

	/// <summary>Resolves the logical parent location.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The parent location, or <see langword="null"/> when none exists.</returns>
	ValueTask<BrowseLocation?> GetParentLocationAsync(CancellationToken cancellationToken = default);
}

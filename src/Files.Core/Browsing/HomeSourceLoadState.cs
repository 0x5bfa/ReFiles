// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Browsing;

/// <summary>Describes one source's independent root-loading state.</summary>
public enum HomeSourceLoadState
{
	/// <summary>Enumeration has not started.</summary>
	NotStarted,
	/// <summary>The source is enumerating roots.</summary>
	Loading,
	/// <summary>Root enumeration completed.</summary>
	Ready,
	/// <summary>The source failed; other sources remain available.</summary>
	Failed,
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Sessions;

/// <summary>Provides consumer access to PaneSession state and operations.</summary>
public interface IPaneSession : IAsyncDisposable
{
	/// <summary>
	/// Gets the stable pane identifier.
	/// </summary>
	Guid Id { get; }

	/// <summary>
	/// Gets the content session owned by this pane.
	/// </summary>
	IPaneContentSession Content { get; }

}

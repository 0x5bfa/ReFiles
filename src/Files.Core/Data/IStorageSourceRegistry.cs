// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Data;

/// <summary>Changes the visible source set without ending the lifetime of borrowed source models.</summary>
public interface IStorageSourceRegistry
{
	/// <summary>Occurs after the visible source collection changes.</summary>
	event EventHandler? SourcesChanged;

	/// <summary>Adds a source, transferring ownership on successful registration, or reactivates the same removed instance.</summary>
	/// <param name="source">The source to register. A different instance cannot reuse an owned source identity.</param>
	void AddSource(IStorageSource source);

	/// <summary>Hides a source from new browsing; its resources remain owned until workspace disposal.</summary>
	/// <param name="sourceId">The source identity.</param>
	/// <returns>Whether a visible source was removed.</returns>
	bool RemoveSource(StorageSourceId sourceId);
}

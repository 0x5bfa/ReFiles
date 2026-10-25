// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Data;
using Files.Core.Storage;

namespace Files.Core.Sessions;

/// <summary>Supplies borrowed application services to the browse session graph.</summary>
public sealed class BrowseApplicationServices
{
	/// <summary>Gets the shared storage workspace.</summary>
	public IStorageWorkspace Workspace { get; }
	/// <summary>Gets the configured operation execution boundary.</summary>
	public IStorageOperationService Operations { get; }
	/// <summary>Gets the optional search routing policy.</summary>
	public IBrowseSearchProvider? Search { get; }

	/// <summary>Initializes the shared browse dependencies.</summary>
	/// <param name="workspace">The borrowed workspace.</param>
	/// <param name="operations">The borrowed operation service.</param>
	/// <param name="search">The optional search provider.</param>
	public BrowseApplicationServices(IStorageWorkspace workspace, IStorageOperationService operations, IBrowseSearchProvider? search = null)
	{
		ArgumentNullException.ThrowIfNull(workspace);

		ArgumentNullException.ThrowIfNull(operations);

		Workspace = workspace;
		Operations = operations;
		Search = search;
	}
}

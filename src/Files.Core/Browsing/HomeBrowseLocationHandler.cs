// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Data;

namespace Files.Core.Browsing;

/// <summary>
/// Opens the aggregate root list for all configured storage sources.
/// </summary>
public sealed class HomeBrowseLocationHandler : IBrowseLocationHandler
{
	private readonly IFilesDataRoot _dataRoot;

	public HomeBrowseLocationHandler(IFilesDataRoot dataRoot)
	{
		ArgumentNullException.ThrowIfNull(dataRoot);

		_dataRoot = dataRoot;
	}

	public bool CanHandle(BrowseLocation location) => location is HomeLocation;

	public ValueTask<IBrowseLocationContext> OpenAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		if (location is not HomeLocation homeLocation)
		{
			throw new ArgumentException("The location must identify the Files home.", nameof(location));
		}

		cancellationToken.ThrowIfCancellationRequested();

		return ValueTask.FromResult<IBrowseLocationContext>(new HomeBrowseLocationContext(homeLocation, _dataRoot));
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.Sessions;

public sealed partial class FilesApplicationSession
{
	IReadOnlyList<IWindowSession> IFilesApplicationSession.Windows => Windows;

	IWindowSession? IFilesApplicationSession.ActiveWindow => ActiveWindow;

	async ValueTask<IWindowSession> IFilesApplicationSession.CreateWindowAsync(BrowseLocation? initialLocation, CancellationToken cancellationToken)
	{
		return await CreateWindowAsync(initialLocation, cancellationToken).ConfigureAwait(false);
	}
}

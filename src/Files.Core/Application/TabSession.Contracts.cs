// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.Sessions;

public sealed partial class TabSession
{
	IReadOnlyList<IPaneSession> ITabSession.Panes => Panes;

	IPaneSession? ITabSession.ActivePane => ActivePane;

	async ValueTask<IPaneSession> ITabSession.OpenSplitAsync(PaneSplitOrientation orientation, CancellationToken cancellationToken)
	{
		return await OpenSplitAsync(orientation, cancellationToken).ConfigureAwait(false);
	}

	async ValueTask<IPaneSession> ITabSession.OpenSplitAsync(PaneSplitOrientation orientation, BrowseLocation? initialLocation, CancellationToken cancellationToken)
	{
		return await OpenSplitAsync(orientation, initialLocation, cancellationToken).ConfigureAwait(false);
	}
}

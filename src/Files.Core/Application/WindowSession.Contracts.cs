// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.Sessions;

public sealed partial class WindowSession
{
	IReadOnlyList<ITabSession> IWindowSession.Tabs => Tabs;

	ITabSession? IWindowSession.ActiveTab => ActiveTab;

	async ValueTask<ITabSession> IWindowSession.OpenTabAsync(BrowseLocation? initialLocation, CancellationToken cancellationToken)
	{
		return await OpenTabAsync(initialLocation, cancellationToken).ConfigureAwait(false);
	}

	async ValueTask<ITabSession> IWindowSession.OpenContentTabAsync(Func<IPaneContentSession> contentFactory, CancellationToken cancellationToken)
	{
		return await OpenContentTabAsync(contentFactory, cancellationToken).ConfigureAwait(false);
	}

	async ValueTask<ITabSession> IWindowSession.OpenRestoredTabAsync(TabRestoreState state, CancellationToken cancellationToken)
	{
		return await OpenRestoredTabAsync(state, cancellationToken).ConfigureAwait(false);
	}

	async ValueTask<ITabSession?> IWindowSession.DuplicateTabAsync(Guid tabId, CancellationToken cancellationToken)
	{
		return await DuplicateTabAsync(tabId, cancellationToken).ConfigureAwait(false);
	}

	async ValueTask<ITabSession?> IWindowSession.ReopenTabAsync(CancellationToken cancellationToken)
	{
		return await ReopenTabAsync(cancellationToken).ConfigureAwait(false);
	}
}

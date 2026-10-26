// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.Sessions;

public sealed partial class BrowsePaneSession
{
	/// <inheritdoc />
	public ValueTask InitializeAsync(CancellationToken cancellationToken = default)
	{
		return _initialHistory is { } history ? RestoreAsync(history, cancellationToken) : NavigateAsync(HomeLocation.Instance, cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask<IPaneContentSession> CreateSplitAsync(CancellationToken cancellationToken = default)
	{
		var content = CaptureRestoreFactory()();
		try
		{
			await content.InitializeAsync(cancellationToken).ConfigureAwait(false);

			return content;
		}
		catch
		{
			await content.DisposeAsync().ConfigureAwait(false);
			throw;
		}
	}

	/// <inheritdoc />
	public Func<IPaneContentSession> CaptureRestoreFactory()
	{
		EnsureActive();

		var factory = _splitFactory ?? throw new NotSupportedException("This browse content has no restoration factory.");
		var history = History.Capture();
		if (history.Current is null)
		{
			history = _initialHistory ?? new BrowseNavigationHistorySnapshot([HomeLocation.Instance], 0);
		}


		return () =>
		{
			var content = factory();
			content._initialHistory = history;

			return content;
		};
	}
}

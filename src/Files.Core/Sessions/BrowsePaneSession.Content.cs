// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.Sessions;

public sealed partial class BrowsePaneSession
{
	/// <inheritdoc />
	public ValueTask InitializeAsync(CancellationToken cancellationToken = default)
	{
		return NavigateAsync(_initialLocation ?? HomeLocation.Instance, cancellationToken: cancellationToken);
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
		var location = Location ?? _initialLocation ?? HomeLocation.Instance;

		return () =>
		{
			var content = factory();
			content._initialLocation = location;

			return content;
		};
	}
}

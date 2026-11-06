// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities;

/// <summary>Pairs a non-owning capability view with cleanup controlled exclusively by the item owner.</summary>
/// <typeparam name="TCapability">The capability contract exposed by the view.</typeparam>
public readonly struct BorrowedCapabilityView<TCapability> where TCapability : class, ICapability
{
	/// <summary>Gets the view returned to consumers.</summary>
	public TCapability View { get; }
	/// <summary>Gets the optional callback that detaches view-owned state without disposing the underlying implementation.</summary>
	public Action? Release { get; }

	/// <summary>Creates a borrowed view registration.</summary>
	/// <param name="view">The view, exposing only the capability contract and its base interfaces.</param>
	/// <param name="release">The optional owner-only cleanup callback; it runs before implementation disposal, including after resolution failure.</param>
	public BorrowedCapabilityView(TCapability view, Action? release = null)
	{
		ArgumentNullException.ThrowIfNull(view);

		View = view;
		Release = release;
	}
}

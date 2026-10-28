// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>Retains transfer effects even when execution or cleanup fails.</summary>
public sealed record StorageTransferOutcome
{
	/// <summary>Gets the acknowledged publication state.</summary>
	public StorageTransferPublication Publication { get; }
	/// <summary>Gets the last execution phase reached.</summary>
	public StorageTransferPhase Phase { get; }
	/// <summary>Gets the actual destination reference, when publication was acknowledged.</summary>
	public StorableReference? Destination { get; }
	/// <summary>Gets the number of bytes copied into staging.</summary>
	public long CompletedBytes { get; }
	/// <summary>Gets the number of acknowledged published items.</summary>
	public int CommittedItems { get; }

	/// <summary>Creates a transfer effect report.</summary>
	/// <param name="publication">The publication state.</param>
	/// <param name="phase">The last execution phase.</param>
	/// <param name="destination">The actual committed destination, when known.</param>
	/// <param name="completedBytes">The number of bytes written.</param>
	/// <param name="committedItems">The acknowledged count, or the count implied by a single-item publication.</param>
	public StorageTransferOutcome(StorageTransferPublication publication, StorageTransferPhase phase, StorableReference? destination, long completedBytes, int? committedItems = null)
	{
		if (!Enum.IsDefined(publication) || !Enum.IsDefined(phase))
		{
			throw new ArgumentOutOfRangeException(nameof(publication));
		}

		ArgumentOutOfRangeException.ThrowIfNegative(completedBytes);

		var count = committedItems ?? (publication == StorageTransferPublication.Committed ? 1 : 0);
		ArgumentOutOfRangeException.ThrowIfNegative(count);

		if ((count > 0) != (destination is not null) || (publication is StorageTransferPublication.Committed or StorageTransferPublication.Partial && count == 0)
			|| (publication == StorageTransferPublication.None && count != 0))
		{
			throw new ArgumentException("Only acknowledged publication can carry a destination reference.", nameof(destination));
		}

		Publication = publication;
		Phase = phase;
		Destination = destination;
		CompletedBytes = completedBytes;
		CommittedItems = count;
	}

	/// <summary>Combines bounded transfer effects without discarding earlier published items on failure.</summary>
	/// <param name="previous">The preceding aggregate, when available.</param>
	/// <param name="current">The current item effects, when available.</param>
	/// <param name="succeeded">Whether the current item succeeded.</param>
	/// <returns>The combined effects, or <see langword="null"/> when no transfer effects were reported.</returns>
	public static StorageTransferOutcome? Accumulate(StorageTransferOutcome? previous, StorageTransferOutcome? current, bool succeeded)
	{
		if (previous is null && current is null)
		{
			return null;
		}

		var count = checked((previous?.CommittedItems ?? 0) + (current?.CommittedItems ?? 0));
		var destination = current?.Destination ?? previous?.Destination;
		var publication = previous?.Publication == StorageTransferPublication.Unknown || current?.Publication == StorageTransferPublication.Unknown ? StorageTransferPublication.Unknown
			: count == 0 ? StorageTransferPublication.None : succeeded ? StorageTransferPublication.Committed : StorageTransferPublication.Partial;

		return new StorageTransferOutcome(publication, current?.Phase ?? previous!.Phase, destination, checked((previous?.CompletedBytes ?? 0) + (current?.CompletedBytes ?? 0)), count);
	}
}

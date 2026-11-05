// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

/// <summary>Selects a reader before exposing any archive items.</summary>
public sealed class ArchiveBackendSelector
{
	/// <summary>Gets readers ordered by descending priority.</summary>
	public IReadOnlyList<IArchiveBackend> Backends { get; }

	/// <summary>Creates a reader selector.</summary>
	/// <param name="backends">The registered readers.</param>
	public ArchiveBackendSelector(IEnumerable<IArchiveBackend> backends)
	{
		ArgumentNullException.ThrowIfNull(backends);

		var readers = backends.ToArray();
		if (readers.Length == 0 || readers.Any(reader => reader is null || string.IsNullOrWhiteSpace(reader.Id)))
		{
			throw new ArgumentException("Supply at least one reader with a nonempty identifier.", nameof(backends));
		}

		if (readers.Select(reader => reader.Id).Distinct(StringComparer.Ordinal).Count() != readers.Length)
		{
			throw new ArgumentException("Reader identifiers must be unique.", nameof(backends));
		}

		Backends = Array.AsReadOnly(readers.OrderByDescending(reader => reader.Priority).ToArray());
	}

	/// <summary>Tries another reader only when a reader explicitly reports unsupported input.</summary>
	/// <param name="request">The borrowed input request.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The first supported reader outcome.</returns>
	public async ValueTask<ArchiveOpenResult> TryOpenAsync(ArchiveOpenRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		foreach (var backend in Backends)
		{
			cancellationToken.ThrowIfCancellationRequested();

			request.Input.Position = 0;
			var result = await backend.TryOpenAsync(request, cancellationToken).ConfigureAwait(false);
			if (result is not ArchiveOpenResult.Unsupported)
			{
				return result;
			}
		}

		return ArchiveOpenResult.Unsupported.Instance;
	}
}

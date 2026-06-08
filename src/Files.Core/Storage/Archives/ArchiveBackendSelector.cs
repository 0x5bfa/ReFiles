// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

/// <summary>
/// Selects exactly one backend before archive items are exposed to a browse session.
/// </summary>
public sealed class ArchiveBackendSelector
{
	private readonly IReadOnlyList<IArchiveBackend> backends;
	private readonly IArchiveProbe? probe;

	public ArchiveBackendSelector(
		IEnumerable<IArchiveBackend> backends,
		IArchiveProbe? probe = null)
	{
		ArgumentNullException.ThrowIfNull(backends);

		var suppliedBackends = backends.ToArray();
		if (suppliedBackends.Length is 0)
		{
			throw new ArgumentException(
				"At least one archive backend is required.",
				nameof(backends));
		}

		if (suppliedBackends.Any(static backend => backend is null))
		{
			throw new ArgumentException(
				"Archive backends cannot contain null values.",
				nameof(backends));
		}

		if (suppliedBackends.Any(
			static backend => string.IsNullOrWhiteSpace(
				backend.Id)))
		{
			throw new ArgumentException(
				"Archive backend IDs cannot be empty.",
				nameof(backends));
		}

		var backendArray = suppliedBackends
			.OrderByDescending(static backend => backend.Priority)
			.ToArray();
		var duplicateId = backendArray
			.GroupBy(
				static backend => backend.Id,
				StringComparer.Ordinal)
			.FirstOrDefault(static group => group.Count() > 1);
		if (duplicateId is not null)
		{
			throw new ArgumentException(
				$"Archive backend ID '{duplicateId.Key}' was supplied more than once.",
				nameof(backends));
		}

		this.backends = Array.AsReadOnly(backendArray);
		this.probe = probe;
	}

	public IReadOnlyList<IArchiveBackend> Backends => backends;

	public async ValueTask<ArchiveMountResult> TryMountAsync(
		ArchiveMountRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();

		ArchiveProbeResult probeResult = ArchiveProbeResult.Unknown;
		Exception? probeError = null;
		if (probe is not null)
		{
			try
			{
				probeResult = await probe
					.ProbeAsync(request, cancellationToken)
					.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
				when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception error)
			{
				probeError = error;
			}
		}

		if (probeResult is
			{
				Kind: ArchiveProbeKind.CredentialRequired,
				Challenge: { } challenge,
			})
		{
			return new ArchiveMountResult.CredentialRequired(
				challenge);
		}

		var requireEncryptedSupport =
			probeResult.Kind is ArchiveProbeKind.Encrypted;
		List<Exception>? errors = probeError is null
			? null
			: [probeError];

		foreach (var backend in backends)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (requireEncryptedSupport
				&& !backend.SupportsEncryptedArchives)
			{
				continue;
			}

			ArchiveMountResult result;
			try
			{
				result = await backend
					.TryMountAsync(request, cancellationToken)
					.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
				when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception error)
			{
				(errors ??= []).Add(error);
				continue;
			}

			switch (result)
			{
				case ArchiveMountResult.Success:
				case ArchiveMountResult.CredentialRequired:
					return result;
				case ArchiveMountResult.Failed failed:
					(errors ??= []).Add(failed.Error);
					break;
				case ArchiveMountResult.Unsupported:
					break;
				default:
					throw new InvalidOperationException(
						$"Archive backend '{backend.Id}' returned an unknown result.");
			}
		}

		return errors switch
		{
			null or { Count: 0 } =>
				ArchiveMountResult.Unsupported.Instance,
			{ Count: 1 } =>
				new ArchiveMountResult.Failed(errors[0]),
			_ =>
				new ArchiveMountResult.Failed(
					new AggregateException(
						"No archive backend could mount the item.",
						errors)),
		};
	}
}

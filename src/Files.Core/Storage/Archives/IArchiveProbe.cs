// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

/// <summary>Inspects an archive before backend selection.</summary>
public interface IArchiveProbe
{
	/// <summary>Probes an archive mount request.</summary>
	/// <param name="request">The mount request.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The probe result.</returns>
	ValueTask<ArchiveProbeResult> ProbeAsync(ArchiveMountRequest request, CancellationToken cancellationToken = default);
}

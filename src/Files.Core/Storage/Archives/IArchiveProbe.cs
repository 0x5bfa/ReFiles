// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

public interface IArchiveProbe
{
	ValueTask<ArchiveProbeResult> ProbeAsync(
		ArchiveMountRequest request,
		CancellationToken cancellationToken = default);
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Previews;

public interface IWindowsShellPreviewSessionFactory
{
	ValueTask<IWindowsShellPreviewSession> CreateAsync(WindowsShellPreviewResult result, WindowsPreviewHost host, CancellationToken cancellationToken = default);
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Previews;

public interface IWindowsShellPreviewSession : IAsyncDisposable
{
	ValueTask SetBoundsAsync(WindowsPreviewBounds bounds, CancellationToken cancellationToken = default);

	ValueTask SetThemeAsync(WindowsPreviewColor background, WindowsPreviewColor foreground, CancellationToken cancellationToken = default);

	ValueTask SetFocusAsync(CancellationToken cancellationToken = default);

	ValueTask<nint> QueryFocusAsync(CancellationToken cancellationToken = default);

	ValueTask<bool> TryTranslateAcceleratorAsync(nint messagePointer, CancellationToken cancellationToken = default);
}

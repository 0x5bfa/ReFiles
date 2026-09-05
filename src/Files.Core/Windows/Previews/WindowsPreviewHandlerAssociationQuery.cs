// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Windows.Win32.Foundation;

namespace Files.Core.Windows;

internal delegate HRESULT WindowsPreviewHandlerAssociationQuery(string normalizedExtension, Span<char> buffer, ref uint characterCount);

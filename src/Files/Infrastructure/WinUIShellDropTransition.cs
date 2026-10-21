// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Windows;
using System.Drawing;
using Windows.Win32.System.Com;

namespace Files.Infrastructure;

internal sealed record WinUIShellDropTransition(IDataObject DataObject, WinUIShellDragState DragState, long Generation, CancellationToken CancellationToken);

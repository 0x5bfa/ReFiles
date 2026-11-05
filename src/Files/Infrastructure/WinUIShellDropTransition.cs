// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Windows;

namespace Files.Infrastructure;

internal sealed record WinUIShellDropTransition(WindowsShellDataTransfer DataObject, WinUIShellDragState DragState, long Generation, CancellationToken CancellationToken);

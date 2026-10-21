// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Windows;
using System.Drawing;
using Windows.Win32.System.Com;

namespace Files.Infrastructure;

internal readonly record struct WinUIShellDragState(WindowsShellDragDropModifiers Modifiers, Point ScreenPoint, WindowsShellDropEffects AllowedEffects);

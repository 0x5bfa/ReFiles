// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Windows;
using System.Drawing;

namespace Files.Infrastructure;

internal sealed record WinUIShellDropSession(WindowsShellDropSession Session, WindowsShellDropEffects AcceptedEffect);

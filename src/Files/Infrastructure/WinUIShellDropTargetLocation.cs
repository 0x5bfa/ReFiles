// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Storage.Windows;
using Microsoft.UI.Xaml;
using System.Drawing;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;
using Windows.Win32;
using Windows.Win32.System.Com;

namespace Files.Infrastructure;

internal sealed record WinUIShellDropTargetLocation(StorableReference Reference, bool Background);

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.Core.Windows;

/// <summary>Forwards an accelerator message from a Windows preview handler without waiting for UI processing.</summary>
/// <param name="message">The native keyboard message.</param>
/// <returns><see langword="true"/> when the message was accepted for asynchronous processing.</returns>
public delegate bool WindowsPreviewAcceleratorForwarder(in MSG message);

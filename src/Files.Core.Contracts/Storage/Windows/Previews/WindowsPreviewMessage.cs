// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Windows;

/// <summary>Contains a snapshot of a preview keyboard message without native layout dependencies.</summary>
/// <param name="WindowHandle">The borrowed destination window handle.</param>
/// <param name="MessageId">The Windows message identifier.</param>
/// <param name="WParam">The message's unsigned parameter.</param>
/// <param name="LParam">The message's signed parameter.</param>
/// <param name="Timestamp">The message timestamp.</param>
/// <param name="ScreenX">The screen cursor X coordinate.</param>
/// <param name="ScreenY">The screen cursor Y coordinate.</param>
public readonly record struct WindowsPreviewMessage(nint WindowHandle, uint MessageId, nuint WParam, nint LParam, uint Timestamp, int ScreenX, int ScreenY);

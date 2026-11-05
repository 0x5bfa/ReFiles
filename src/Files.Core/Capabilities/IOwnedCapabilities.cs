// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities;

/// <summary>Owns a composed capability collection. Transfer this collection to the item model rather than exposing it to borrowers.</summary>
public interface IOwnedCapabilities : ICapabilities, IDisposable, IAsyncDisposable
{
}

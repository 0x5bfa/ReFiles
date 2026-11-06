// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;

namespace Files.Core.Capabilities;

/// <summary>Identifies optional item capability contracts. Discovery requires a derived interface, not this marker or an implementation class.</summary>
/// <remarks>Implemented interface metadata is retained so borrowed views can be validated in trimmed applications.</remarks>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
public interface ICapability
{
}

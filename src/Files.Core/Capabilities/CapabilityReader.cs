// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;

namespace Files.Core.Capabilities;

internal sealed class CapabilityReader(ICapabilities capabilities) : ICapabilities
{
	public TCapability? Get<TCapability>() where TCapability : class, ICapability
	{
		return capabilities.Get<TCapability>();
	}

	public bool TryGet<TCapability>([NotNullWhen(true)] out TCapability? capability) where TCapability : class, ICapability
	{
		return capabilities.TryGet(out capability);
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities;

internal static class CapabilityContract<TCapability> where TCapability : class, ICapability
{
	private static readonly bool _isValid = typeof(TCapability).IsInterface && typeof(TCapability) != typeof(ICapability);

	public static void Validate()
	{
		if (!_isValid)
		{
			throw new ArgumentException($"Item capability '{typeof(TCapability).FullName}' must be an interface derived from {nameof(ICapability)}.", nameof(TCapability));
		}
	}
}

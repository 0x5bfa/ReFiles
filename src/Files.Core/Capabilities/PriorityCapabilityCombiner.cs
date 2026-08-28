// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities;

/// <summary>
/// Selects the single highest-priority item capability option.
/// </summary>
public sealed class PriorityCapabilityCombiner<TCapability> : ICapabilityCombiner<TCapability>
	where TCapability : class
{
	/// <summary>Selects the single capability option with the highest priority.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="options">The capability options to evaluate.</param>
	/// <returns>The highest-priority capability, or <see langword="null"/> when no options exist.</returns>
	public TCapability? Combine(ItemContext context, IReadOnlyList<CapabilityOption<TCapability>> options)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(options);

		if (options.Count is 0)
		{
			return null;
		}

		var highestPriority = options.Max(static option => option.Priority);
		var matches = options.Where(option => option.Priority == highestPriority).ToArray();

		if (matches.Length is not 1)
		{
			throw new InvalidOperationException($"Item capability '{typeof(TCapability).FullName}' has more than one option at priority {highestPriority}.");
		}

		return matches[0].Capability;
	}
}

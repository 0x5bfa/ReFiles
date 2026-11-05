// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;
using Files.Core.Capabilities.Content;

namespace Files.Core.Storage.Archives;

internal sealed class ArchiveContentSourceFactory : ICapabilityFactory<IReadableContent>
{
	/// <summary>Creates explicitly registered, borrowed read access for this provider.</summary>
	/// <param name="context">The item context.</param>
	/// <returns>The read capability, or null when the provider does not apply.</returns>
	public IReadableContent? Create(ItemContext context)
	{
		return context.Source is ArchiveStorageSource ? context.CoreModel as IReadableContent : null;
	}
}

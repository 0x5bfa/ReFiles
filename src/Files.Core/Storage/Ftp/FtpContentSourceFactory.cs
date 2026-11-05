// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;
using Files.Core.Capabilities.Content;

namespace Files.Core.Storage.Ftp;

internal sealed class FtpContentSourceFactory : ICapabilityFactory<IReadableContent>, ICapabilityFactory<IWritableContent>
{
	/// <summary>Creates explicitly registered, borrowed read access for this provider.</summary>
	/// <param name="context">The item context.</param>
	/// <returns>The read capability, or null when the provider does not apply.</returns>
	public IReadableContent? Create(ItemContext context)
	{
		return context.Source is FtpStorageSource && context.CoreModel is FtpFile file ? file : null;
	}

	IWritableContent? ICapabilityFactory<IWritableContent>.Create(ItemContext context)
	{
		return context.Source is FtpStorageSource && context.CoreModel is FtpFile file ? file : null;
	}
}

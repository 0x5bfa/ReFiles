// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0
using Files.Core.Capabilities;

internal interface ICustomCapability : ICapability { }

internal sealed class CustomFactory : ICapabilityFactory<ICustomCapability>
{
	public ICustomCapability? Create(ItemContext context)
	{
		return null;
	}
}

internal static class Registration
{
	internal static void Register(ICapabilityBuilder builder)
	{
		builder.Add<ICustomCapability>(new CustomFactory()).SetBorrowedView<ICustomCapability>(static _ => new CustomView());
	}

	private sealed class CustomView : ICustomCapability { }
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Previews;

[Flags]
public enum WindowsPreviewHandlerActivationContext : uint
{
	InProcessServer = 0x1,
	LocalServer = 0x4,
}

public interface IWindowsPreviewHandlerActivationPolicy
{
	WindowsPreviewHandlerActivationContext GetContext(Guid handlerClsid);
}

public sealed class LocalServerWindowsPreviewHandlerActivationPolicy
    : IWindowsPreviewHandlerActivationPolicy
{
	public WindowsPreviewHandlerActivationContext GetContext(Guid handlerClsid)
	{
		if (handlerClsid == Guid.Empty)
		{
			throw new ArgumentException("A preview handler CLSID is required.", nameof(handlerClsid));
		}

		return WindowsPreviewHandlerActivationContext.LocalServer;
	}
}

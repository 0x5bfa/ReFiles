// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities.Previews;

/// <summary>Specifies the process context allowed for preview handler activation.</summary>
[Flags]
public enum WindowsPreviewHandlerActivationContext : uint
{
	/// <summary>Activate an in-process preview handler.</summary>
	InProcessServer = 0x1,
	/// <summary>Activate a local-server preview handler.</summary>
	LocalServer = 0x4,
}

/// <summary>Chooses the activation context for a preview handler.</summary>
public interface IWindowsPreviewHandlerActivationPolicy
{
	/// <summary>Gets the activation context for a handler.</summary>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns>The permitted activation context.</returns>
	WindowsPreviewHandlerActivationContext GetContext(Guid handlerClsid);
}

/// <summary>Activates preview handlers through a local server.</summary>
public sealed class LocalServerWindowsPreviewHandlerActivationPolicy
    : IWindowsPreviewHandlerActivationPolicy
{
	/// <summary>Gets the local-server activation context.</summary>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns><see cref="WindowsPreviewHandlerActivationContext.LocalServer"/>.</returns>
	public WindowsPreviewHandlerActivationContext GetContext(Guid handlerClsid)
	{
		if (handlerClsid == Guid.Empty)
		{
			throw new ArgumentException("A preview handler CLSID is required.", nameof(handlerClsid));
		}

		return WindowsPreviewHandlerActivationContext.LocalServer;
	}
}

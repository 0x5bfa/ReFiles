// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Files.Core.Capabilities;
using Files.Core.Capabilities.Previews;
using Files.Core.Storage;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.Core.Windows;

/// <summary>Allows registered Shell preview handlers.</summary>
public sealed class AllowWindowsShellPreviewPolicy : IWindowsShellPreviewPolicy
{
	/// <summary>Gets the shared policy instance.</summary>
	public static AllowWindowsShellPreviewPolicy Instance { get; } = new();

	private AllowWindowsShellPreviewPolicy()
	{
	}

	/// <summary>Allows the specified preview handler without request-specific limits.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns><see langword="null"/> because the handler is not blocked.</returns>
	public PreviewBlockReason? GetBlockReason(ItemContext context, Guid handlerClsid)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (handlerClsid == Guid.Empty)
		{
			throw new ArgumentException("A preview handler CLSID is required.", nameof(handlerClsid));
		}

		return null;
	}

	/// <summary>Allows the specified preview handler.</summary>
	/// <param name="request">The preview request.</param>
	/// <param name="context">The item context.</param>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A completed task with no blocking reason.</returns>
	public ValueTask<PreviewBlockReason?> GetBlockReasonAsync(PreviewRequest request, ItemContext context, Guid handlerClsid, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(context);

		if (handlerClsid == Guid.Empty)
		{
			throw new ArgumentException("A preview handler CLSID is required.", nameof(handlerClsid));
		}

		cancellationToken.ThrowIfCancellationRequested();

		return ValueTask.FromResult<PreviewBlockReason?>(null);
	}
}

/// <summary>Describes the bounds of a Windows preview host.</summary>
public readonly record struct WindowsPreviewBounds
{
	/// <summary>Gets the horizontal position.</summary>
	public int X { get; }
	/// <summary>Gets the vertical position.</summary>
	public int Y { get; }
	/// <summary>Gets the host width.</summary>
	public int Width { get; }
	/// <summary>Gets the host height.</summary>
	public int Height { get; }

	/// <summary>Initializes preview bounds.</summary>
	/// <param name="x">The horizontal position.</param>
	/// <param name="y">The vertical position.</param>
	/// <param name="width">The host width.</param>
	/// <param name="height">The host height.</param>
	public WindowsPreviewBounds(int x, int y, int width, int height)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(width);
		ArgumentOutOfRangeException.ThrowIfNegative(height);

		X = x;
		Y = y;
		Width = width;
		Height = height;
	}
}

/// <summary>Describes an RGB color used by a Windows preview handler.</summary>
/// <param name="Red">The red component.</param>
/// <param name="Green">The green component.</param>
/// <param name="Blue">The blue component.</param>
public readonly record struct WindowsPreviewColor(byte Red, byte Green, byte Blue);

/// <summary>Forwards an accelerator message from a Windows preview handler without waiting for UI processing.</summary>
/// <param name="message">The native keyboard message.</param>
/// <returns><see langword="true"/> when the message was accepted for asynchronous processing.</returns>
public delegate bool WindowsPreviewAcceleratorForwarder(in MSG message);

/// <summary>Identifies the native window that hosts a preview handler.</summary>
public sealed record WindowsPreviewHost
{
	/// <summary>Gets the native host window handle.</summary>
	public HWND WindowHandle { get; }
	/// <summary>Gets the host bounds.</summary>
	public WindowsPreviewBounds Bounds { get; }
	/// <summary>Gets the callback that asynchronously forwards accelerator messages.</summary>
	public WindowsPreviewAcceleratorForwarder? AcceleratorForwarder { get; }

	/// <summary>Initializes a preview host.</summary>
	/// <param name="windowHandle">The native host window handle.</param>
	/// <param name="bounds">The host bounds.</param>
	public WindowsPreviewHost(HWND windowHandle, WindowsPreviewBounds bounds) : this(windowHandle, bounds, null)
	{
	}

	/// <summary>Initializes a preview host.</summary>
	/// <param name="windowHandle">The native host window handle.</param>
	/// <param name="bounds">The host bounds.</param>
	/// <param name="acceleratorForwarder">The callback that asynchronously forwards accelerator messages.</param>
	public WindowsPreviewHost(HWND windowHandle, WindowsPreviewBounds bounds, WindowsPreviewAcceleratorForwarder? acceleratorForwarder)
	{
		if (windowHandle.IsNull)
		{
			throw new ArgumentException("A preview host window handle is required.", nameof(windowHandle));
		}

		if (!PInvoke.IsWindow(windowHandle))
		{
			throw new ArgumentException("The preview host window handle is not valid.", nameof(windowHandle));
		}

		WindowHandle = windowHandle;
		Bounds = bounds;
		AcceleratorForwarder = acceleratorForwarder;
	}
}

/// <summary>Specifies the process context allowed for preview handler activation.</summary>
[Flags]
public enum WindowsPreviewHandlerActivationContext : uint
{
	/// <summary>Activate an in-process preview handler.</summary>
	InProcessServer = 0x1,
	/// <summary>Activate a local-server preview handler.</summary>
	LocalServer = 0x4,
	/// <summary>Allow COM to select an in-process or local-server preview handler according to its registration.</summary>
	ShellDefault = InProcessServer | LocalServer,
	/// <summary>Use the caller's impersonation token when activating the local server.</summary>
	EnableCloaking = 0x100000,
}

/// <summary>Describes the lifecycle state of a Windows Shell preview session.</summary>
public enum WindowsShellPreviewSessionState
{
	/// <summary>The session has been created.</summary>
	Created,
	/// <summary>The preview handler is being activated.</summary>
	Activating,
	/// <summary>The preview handler has been initialized.</summary>
	Initialized,
	/// <summary>The preview handler is rendering a preview.</summary>
	Previewing,
	/// <summary>Activation or rendering failed.</summary>
	Faulted,
	/// <summary>The session has been disposed.</summary>
	Disposed,
}

/// <summary>Indicates that a Shell preview became unsafe before handler activation.</summary>
public sealed class WindowsShellPreviewBlockedException : InvalidOperationException
{
	/// <summary>Gets the reason the preview was blocked.</summary>
	public PreviewBlockReason Reason { get; }

	/// <summary>Initializes a blocked Shell preview exception.</summary>
	/// <param name="reason">The reason the preview was blocked.</param>
	public WindowsShellPreviewBlockedException(PreviewBlockReason reason) : base($"The Windows Shell preview was blocked: {reason}.")
	{
		if (reason is not PreviewBlockReason.RequiresHydration and not PreviewBlockReason.TooLarge and not PreviewBlockReason.AccessDenied
			and not PreviewBlockReason.Untrusted and not PreviewBlockReason.DisabledByPolicy)
		{
			throw new ArgumentOutOfRangeException(nameof(reason));
		}

		Reason = reason;
	}
}

/// <summary>Describes a Shell preview handler without owning Shell or UI resources.</summary>
public sealed class WindowsShellPreviewResult : PreviewResult
{
	/// <summary>Gets the storage reference to preview.</summary>
	public StorableReference Reference { get; }
	/// <summary>Gets the preview handler CLSID.</summary>
	public Guid HandlerClsid { get; }
	internal PreviewRequest Request { get; }

	/// <summary>Initializes a Windows Shell preview result that requires trusted content.</summary>
	/// <param name="reference">The storage reference to preview.</param>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	public WindowsShellPreviewResult(StorableReference reference, Guid handlerClsid) : this(reference, handlerClsid, new PreviewRequest())
	{
	}

	internal WindowsShellPreviewResult(StorableReference reference, Guid handlerClsid, PreviewRequest request)
	{
		ArgumentNullException.ThrowIfNull(reference);
		ArgumentNullException.ThrowIfNull(request);

		if (handlerClsid == Guid.Empty)
		{
			throw new ArgumentException("A preview handler CLSID is required.", nameof(handlerClsid));
		}

		Reference = reference;
		HandlerClsid = handlerClsid;
		Request = request;
	}
}

/// <summary>Chooses the activation context for a preview handler.</summary>
public interface IWindowsPreviewHandlerActivationPolicy
{
	/// <summary>Gets the activation context for a handler.</summary>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns>The permitted activation context.</returns>
	WindowsPreviewHandlerActivationContext GetContext(Guid handlerClsid);
}

/// <summary>Activates preview handlers using their registered in-process or local-server COM class.</summary>
public sealed class LocalServerWindowsPreviewHandlerActivationPolicy : IWindowsPreviewHandlerActivationPolicy
{
	/// <summary>Gets the local-server activation context.</summary>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns>An activation context that supports both registered server types.</returns>
	public WindowsPreviewHandlerActivationContext GetContext(Guid handlerClsid)
	{
		if (handlerClsid == Guid.Empty)
		{
			throw new ArgumentException("A preview handler CLSID is required.", nameof(handlerClsid));
		}

		return WindowsPreviewHandlerActivationContext.ShellDefault;
	}
}

/// <summary>Looks up Windows preview handlers by file extension.</summary>
public interface IWindowsPreviewHandlerAssociation
{
	/// <summary>Gets the preview handler CLSID for a normalized extension.</summary>
	/// <param name="normalizedExtension">The normalized extension, including its leading period.</param>
	/// <returns>The handler CLSID string, or <see langword="null"/> when none is registered.</returns>
	string? QueryPreviewHandler(string normalizedExtension);
}

/// <summary>Creates Windows preview handler controllers.</summary>
public interface IWindowsPreviewHandlerControllerFactory
{
	/// <summary>Creates a controller for a preview handler CLSID.</summary>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns>The created controller.</returns>
	IWindowsPreviewHandlerController Create(Guid handlerClsid);
}

/// <summary>Determines whether a Windows preview handler is registered for use by the Shell preview host.</summary>
public interface IWindowsPreviewHandlerRegistrationAllowlist
{
	/// <summary>Determines whether the specified preview handler is registered.</summary>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns><see langword="true"/> when the handler is registered; otherwise, <see langword="false"/>.</returns>
	bool IsRegistered(Guid handlerClsid);
}

/// <summary>Resolves the Windows preview handler registered for an item.</summary>
public interface IWindowsPreviewHandlerResolver
{
	/// <summary>Resolves a preview handler CLSID.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The handler CLSID, or <see langword="null"/> when none is available.</returns>
	ValueTask<Guid?> ResolveAsync(ItemContext context, CancellationToken cancellationToken = default);
}

/// <summary>Resolves items to Windows preview targets.</summary>
public interface IWindowsPreviewTargetResolver
{
	/// <summary>Resolves a preview target for a storage reference.</summary>
	/// <param name="reference">The storage reference.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The resolved preview target.</returns>
	ValueTask<WindowsPreviewTarget> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default);
}

/// <summary>Controls whether Windows Shell preview handlers may run for an item.</summary>
public interface IWindowsShellPreviewPolicy
{
	/// <summary>Gets the reason a handler is blocked without request-specific limits.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns>The blocking reason, or <see langword="null"/> when the handler is allowed.</returns>
	PreviewBlockReason? GetBlockReason(ItemContext context, Guid handlerClsid);

	/// <summary>Gets the reason a handler is blocked for a specific request.</summary>
	/// <param name="request">The preview request.</param>
	/// <param name="context">The item context.</param>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns>The blocking reason, or <see langword="null"/> when the handler is allowed.</returns>
	PreviewBlockReason? GetBlockReason(PreviewRequest request, ItemContext context, Guid handlerClsid)
	{
		ArgumentNullException.ThrowIfNull(request);

		return GetBlockReason(context, handlerClsid);
	}

	/// <summary>Gets the reason a handler is blocked.</summary>
	/// <param name="request">The preview request.</param>
	/// <param name="context">The item context.</param>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The blocking reason, or <see langword="null"/> when the handler is allowed.</returns>
	ValueTask<PreviewBlockReason?> GetBlockReasonAsync(PreviewRequest request, ItemContext context, Guid handlerClsid, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();

		return ValueTask.FromResult(GetBlockReason(request, context, handlerClsid));
	}
}

/// <summary>Provides asynchronous control over a Windows Shell preview session.</summary>
public interface IWindowsShellPreviewSession : IAsyncDisposable
{
	/// <summary>Updates the preview bounds.</summary>
	/// <param name="bounds">The preview bounds.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	ValueTask SetBoundsAsync(WindowsPreviewBounds bounds, CancellationToken cancellationToken = default);
	/// <summary>Updates the preview colors.</summary>
	/// <param name="background">The background color.</param>
	/// <param name="foreground">The foreground color.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	ValueTask SetThemeAsync(WindowsPreviewColor background, WindowsPreviewColor foreground, CancellationToken cancellationToken = default);
	/// <summary>Gives focus to the preview handler.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	ValueTask SetFocusAsync(CancellationToken cancellationToken = default);
	/// <summary>Gets the window that currently has preview focus.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The focused window handle, or zero when no window has focus.</returns>
	ValueTask<HWND> QueryFocusAsync(CancellationToken cancellationToken = default);
}

/// <summary>Creates Windows Shell preview sessions.</summary>
public interface IWindowsShellPreviewSessionFactory
{
	/// <summary>Creates a session for a resolved preview handler and host.</summary>
	/// <param name="result">The resolved preview handler.</param>
	/// <param name="host">The preview host window.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The created preview session.</returns>
	ValueTask<IWindowsShellPreviewSession> CreateAsync(WindowsShellPreviewResult result, WindowsPreviewHost host, CancellationToken cancellationToken = default);
}

/// <summary>Controls the lifetime and rendering surface of a Windows preview handler.</summary>
public interface IWindowsPreviewHandlerController : IDisposable
{
	/// <summary>Sets the preview handler site.</summary>
	void SetSite();
	/// <summary>Sets the preview handler site and accelerator forwarding callback.</summary>
	/// <param name="hostWindow">The preview host window.</param>
	/// <param name="acceleratorForwarder">The callback that asynchronously forwards accelerator messages.</param>
	void SetSite(HWND hostWindow, WindowsPreviewAcceleratorForwarder? acceleratorForwarder)
	{
		SetSite();
	}
	/// <summary>Initializes the handler with a file stream path.</summary>
	/// <param name="fileSystemPath">The path of the file to preview.</param>
	/// <returns><see langword="true"/> when initialization succeeds.</returns>
	bool TryInitializeWithStream(string fileSystemPath);
	/// <summary>Initializes the handler with a Shell parsing name.</summary>
	/// <param name="parsingName">The Shell parsing name.</param>
	/// <returns><see langword="true"/> when initialization succeeds.</returns>
	bool TryInitializeWithItem(string parsingName);
	/// <summary>Initializes the handler with a file path.</summary>
	/// <param name="fileSystemPath">The path of the file to preview.</param>
	/// <returns><see langword="true"/> when initialization succeeds.</returns>
	bool TryInitializeWithFile(string fileSystemPath);
	/// <summary>Associates the handler with its host window.</summary>
	/// <param name="windowHandle">The host window handle.</param>
	/// <param name="bounds">The preview bounds.</param>
	void SetWindow(HWND windowHandle, WindowsPreviewBounds bounds);
	/// <summary>Updates the preview bounds.</summary>
	/// <param name="bounds">The preview bounds.</param>
	void SetBounds(WindowsPreviewBounds bounds);
	/// <summary>Updates the preview colors.</summary>
	/// <param name="background">The background color.</param>
	/// <param name="foreground">The foreground color.</param>
	void SetTheme(WindowsPreviewColor background, WindowsPreviewColor foreground);
	/// <summary>Applies the Windows system preview colors and font when supported.</summary>
	void ApplySystemVisuals()
	{
	}
	/// <summary>Starts preview rendering.</summary>
	void DoPreview();
	/// <summary>Gives keyboard focus to the preview handler.</summary>
	void SetFocus();
	/// <summary>Gets the window that currently has preview focus.</summary>
	/// <returns>The focused window handle, or zero when no window has focus.</returns>
	HWND QueryFocus();
}

internal interface IWindowsPreviewEnterpriseIdResolver
{
	bool HasEnterpriseId(ItemContext context);
}

internal interface IWindowsPreviewFileMetadataResolver
{
	WindowsPreviewFileMetadata? GetMetadata(ItemContext context);
}

internal interface IWindowsPreviewHandlerRegistrationValidator
{
	bool IsCurrentHandler(ItemContext context, Guid handlerClsid);
}

internal interface IWindowsPreviewHandlerTrustResolver
{
	bool AllowsUntrustedPreviews(Guid handlerClsid);
}

internal interface IWindowsPreviewTrustResolver
{
	WindowsPreviewTrustResult GetTrust(ItemContext context);
}

internal readonly record struct WindowsPreviewFileMetadata(uint Attributes, long Length);
internal readonly record struct WindowsPreviewTrustResult(WindowsPreviewTrustStatus Status);
internal enum WindowsPreviewTrustStatus
{
	Allowed,
	Blocked,
	Indeterminate,
}

internal delegate HRESULT WindowsPreviewHandlerAssociationQuery(string normalizedExtension, Span<char> buffer, ref uint characterCount);

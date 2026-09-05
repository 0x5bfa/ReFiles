// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Data;
using Files.Core.Storage;
using Files.Core.Windows;

namespace Files.Activation;

internal enum ItemActivationOutcome
{
	Navigate,
	Invoked,
	Unsupported,
}

internal sealed record ItemActivationRequest(StorableReference Reference, bool IsFolder, string? WorkingDirectory, WindowsShellInvocationPoint? InvocationPoint);

internal interface IItemActivationService
{
	Task<ItemActivationOutcome> ActivateAsync(ItemActivationRequest request, CancellationToken cancellationToken = default);
}

internal interface IWindowsShellDefaultCommandService
{
	Task<ShellDefaultCommand?> GetDefaultCommandAsync(StorableReference reference, CancellationToken cancellationToken = default);

	Task<bool> InvokeDefaultCommandAsync(StorableReference reference, WindowsShellInvocationContext context, CancellationToken cancellationToken = default);
}

internal sealed record ShellDefaultCommand(string? CanonicalVerb);

internal sealed class ItemActivationService : IItemActivationService
{
	private readonly StorageSourceId? _windowsSourceId;
	private readonly nint _ownerWindowHandle;
	private readonly IWindowsShellDefaultCommandService? _windowsShell;

	internal ItemActivationService(IStorageWorkspace workspace, nint ownerWindowHandle)
	{
		ArgumentNullException.ThrowIfNull(workspace);

		var windowsSource = workspace.Sources.OfType<WindowsStorageSource>().FirstOrDefault();
		_windowsSourceId = windowsSource?.SourceId;
		_ownerWindowHandle = ownerWindowHandle;
		_windowsShell = windowsSource is null ? null : new WindowsShellDefaultCommandService(new WindowsShellDefaultCommandInvoker(windowsSource));
	}

	internal ItemActivationService(StorageSourceId? windowsSourceId, nint ownerWindowHandle, IWindowsShellDefaultCommandService? windowsShell)
	{
		_windowsSourceId = windowsSourceId;
		_ownerWindowHandle = ownerWindowHandle;
		_windowsShell = windowsShell;
	}

	public async Task<ItemActivationOutcome> ActivateAsync(ItemActivationRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (_windowsSourceId is null || request.Reference.SourceId != _windowsSourceId || _windowsShell is null)
		{
			return request.IsFolder ? ItemActivationOutcome.Navigate : ItemActivationOutcome.Unsupported;
		}

		if (request.IsFolder)
		{
			var defaultCommand = await _windowsShell.GetDefaultCommandAsync(request.Reference, cancellationToken).ConfigureAwait(false);
			if (defaultCommand is null)
			{
				return IsFileSystemReference(request.Reference) ? ItemActivationOutcome.Navigate : ItemActivationOutcome.Unsupported;
			}

			if (IsNavigationVerb(defaultCommand.CanonicalVerb) || defaultCommand.CanonicalVerb is null && IsFileSystemReference(request.Reference))
			{
				return ItemActivationOutcome.Navigate;
			}
		}

		if (_ownerWindowHandle is 0)
		{
			return ItemActivationOutcome.Unsupported;
		}

		var context = new WindowsShellInvocationContext(_ownerWindowHandle, request.WorkingDirectory, request.InvocationPoint);
		var invoked = await _windowsShell.InvokeDefaultCommandAsync(request.Reference, context, cancellationToken).ConfigureAwait(false);

		return invoked ? ItemActivationOutcome.Invoked : ItemActivationOutcome.Unsupported;
	}

	internal static IItemActivationService CreateStorageOnly() => new ItemActivationService(null, 0, null);

	private static bool IsNavigationVerb(string? verb)
	{
		return verb is not null && (verb.Equals("open", StringComparison.OrdinalIgnoreCase) || verb.Equals("explore", StringComparison.OrdinalIgnoreCase));
	}

	private static bool IsFileSystemReference(StorableReference reference)
	{
		return reference.LastKnownAddress is { } address && address.Scheme.Equals(WindowsStorageSource.FileAddressScheme, StringComparison.OrdinalIgnoreCase);
	}
}

internal sealed class WindowsShellDefaultCommandService : IWindowsShellDefaultCommandService
{
	private readonly WindowsShellDefaultCommandInvoker _invoker;

	internal WindowsShellDefaultCommandService(WindowsShellDefaultCommandInvoker invoker)
	{
		ArgumentNullException.ThrowIfNull(invoker);

		_invoker = invoker;
	}

	public async Task<ShellDefaultCommand?> GetDefaultCommandAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		var command = await _invoker.GetDefaultCommandAsync(reference, cancellationToken).ConfigureAwait(false);

		return command is null ? null : new ShellDefaultCommand(command.CanonicalVerb);
	}

	public Task<bool> InvokeDefaultCommandAsync(StorableReference reference, WindowsShellInvocationContext context, CancellationToken cancellationToken = default)
	{
		return _invoker.InvokeDefaultCommandAsync(reference, context, cancellationToken);
	}
}

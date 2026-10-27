// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Activation;
using Files.Core.Browsing;
using Files.Core.Windows;

namespace Files.Commands.Handlers;

internal sealed class OpenItemCommandHandler : ICommandHandler
{
	private readonly IItemActivationService _activationService;

	public CommandId Id => CommandIds.OpenItem;

	public CommandConcurrencyPolicy ConcurrencyPolicy => CommandConcurrencyPolicy.CancelPrevious;

	public CommandStateInvalidation StateDependencies => CommandStateInvalidation.ActiveTab | CommandStateInvalidation.Loading | CommandStateInvalidation.Location;

	internal OpenItemCommandHandler(IItemActivationService activationService)
	{
		ArgumentNullException.ThrowIfNull(activationService);

		_activationService = activationService;
	}

	public CommandState GetState(CommandContext context)
	{
		return new(context.ActiveFolderBrowser is not null, context.ActiveFolderBrowser is not null && context.InvokedItem is not null);
	}

	public async ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
	{
		if (context.ActiveFolderBrowser is not { } browser || context.InvokedReference is not { } reference)
		{
			return CommandExecutionResult.Unsupported();
		}

		var request = new ItemActivationRequest(reference, context.InvokedItemIsFolder, GetWorkingDirectory(context.Location), context.InvocationPoint);
		var location = await browser.GetItemLocationAsync(reference, cancellationToken).ConfigureAwait(false);
		if (location is not null)
		{
			if (!context.IsCurrent(CommandExecutionScope.Pane))
			{
				return CommandExecutionResult.Canceled();
			}

			await browser.NavigateToLocationAsync(location, cancellationToken).ConfigureAwait(false);

			return CommandExecutionResult.Succeeded();
		}

		var outcome = await _activationService.ActivateAsync(request, cancellationToken).ConfigureAwait(false);
		if (outcome is ItemActivationOutcome.Navigate)
		{
			if (!context.IsCurrent(CommandExecutionScope.Pane))
			{
				return CommandExecutionResult.Canceled();
			}

			await browser.NavigateToReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
		}

		return outcome is ItemActivationOutcome.Unsupported ? CommandExecutionResult.Unsupported() : CommandExecutionResult.Succeeded();
	}

	private static string? GetWorkingDirectory(BrowseLocation? location)
	{
		if (location is FolderLocation { Folder.LastKnownAddress: { } address } && address.Scheme.Equals(WindowsStorageSource.FileAddressScheme, StringComparison.OrdinalIgnoreCase))
		{
			return address.Value;
		}

		return null;
	}
}

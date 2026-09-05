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
		if (context.ActiveFolderBrowser is not { } browser || context.InvokedItem is not { } item)
		{
			return CommandExecutionResult.Unsupported();
		}

		var request = new ItemActivationRequest(item.Reference, item.IsFolder, GetWorkingDirectory(browser.Location), context.InvocationPoint);
		var outcome = await _activationService.ActivateAsync(request, cancellationToken).ConfigureAwait(false);
		if (outcome is ItemActivationOutcome.Navigate)
		{
			await browser.NavigateToItemAsync(item, cancellationToken).ConfigureAwait(false);
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

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Files.Localization;

namespace Files.Commands.Handlers;

internal sealed class NavigationCommandHandler(CommandId id) : ICommandHandler
{
	public CommandId Id => id;

	public CommandConcurrencyPolicy ConcurrencyPolicy =>
		CommandConcurrencyPolicy.CancelPrevious;

	public CommandState GetState(CommandContext context)
	{
		var browser = context.ActiveFolderBrowser;
		if (browser is null)
		{
			return new(false, false);
		}

		var isAvailable = id switch
		{
			var commandId when commandId == CommandIds.NavigateBack =>
				browser.CanGoBack,
			var commandId when commandId == CommandIds.NavigateForward =>
				browser.CanGoForward,
			var commandId when commandId == CommandIds.NavigateUp =>
				browser.CanGoUp,
			var commandId when commandId == CommandIds.NavigatePath =>
				true,
			var commandId when commandId == CommandIds.OpenItem =>
				context.InvokedItem is not null,
			_ => true,
		};

		return new(true, isAvailable && !browser.IsLoading);
	}

	public async ValueTask<CommandExecutionResult> ExecuteAsync(
		CommandContext context,
		CancellationToken cancellationToken = default)
	{
		if (context.ActiveFolderBrowser is not { } browser)
		{
			return CommandExecutionResult.Unsupported();
		}

		switch (id)
		{
			case var commandId when commandId == CommandIds.NavigateBack:
				await browser.GoBackAsync(cancellationToken).ConfigureAwait(false);
				break;
			case var commandId when commandId == CommandIds.NavigateForward:
				await browser.GoForwardAsync(cancellationToken).ConfigureAwait(false);
				break;
			case var commandId when commandId == CommandIds.NavigateUp:
				await browser.GoUpAsync(cancellationToken).ConfigureAwait(false);
				break;
			case var commandId when commandId == CommandIds.NavigateHome:
				await browser.NavigateHomeAsync(cancellationToken)
					.ConfigureAwait(false);
				break;
			case var commandId when commandId == CommandIds.NavigatePath:
				if (string.IsNullOrWhiteSpace(context.Path))
				{
					return CommandExecutionResult.Failed(
						new ArgumentException(
							Strings.FolderPathRequired.GetLocalized(),
							nameof(context.Path)));
				}

				await browser.NavigateToPathAsync(
					context.Path,
					cancellationToken).ConfigureAwait(false);
				break;
			case var commandId when commandId == CommandIds.Refresh:
				await browser.RefreshAsync(cancellationToken).ConfigureAwait(false);
				break;
			case var commandId when commandId == CommandIds.OpenItem:
				if (context.InvokedItem is not { } item)
				{
					return CommandExecutionResult.Unsupported();
				}

				await browser.NavigateToItemAsync(
					item,
					cancellationToken).ConfigureAwait(false);
				break;
			default:
				throw new InvalidOperationException(
					$"Unsupported navigation command '{id}'.");
		}

		return CommandExecutionResult.Succeeded();
	}
}

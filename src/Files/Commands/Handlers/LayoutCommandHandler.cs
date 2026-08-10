// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ViewSettings;
using Files.ViewModels;

namespace Files.Commands.Handlers;

internal sealed class LayoutCommandHandler(CommandId id) : ICommandHandler
{
	public CommandId Id => id;

	public CommandConcurrencyPolicy ConcurrencyPolicy =>
		CommandConcurrencyPolicy.CancelPrevious;

	public CommandStateInvalidation StateDependencies =>
		CommandStateInvalidation.ActiveTab |
		CommandStateInvalidation.Loading |
		CommandStateInvalidation.ViewSettings;

	public CommandState GetState(CommandContext context)
	{
		if (context.ActiveFolderBrowser is not { } browser)
		{
			return new(false, false);
		}

		var layoutMode = GetLayoutMode(id);

		return new(true, !browser.IsLoading, browser.ViewMode == ToFolderViewMode(layoutMode));
	}

	public async ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
	{
		if (context.ActiveFolderBrowser is not { } browser)
		{
			return CommandExecutionResult.Unsupported();
		}

		await browser.SetViewModeAsync(ToFolderViewMode(GetLayoutMode(id)), cancellationToken).ConfigureAwait(false);

		return CommandExecutionResult.Succeeded();
	}

	private static ViewLayoutMode GetLayoutMode(CommandId commandId) =>
		commandId switch
		{
			var id when id == CommandIds.LayoutDetails => ViewLayoutMode.Details,
			var id when id == CommandIds.LayoutList => ViewLayoutMode.List,
			var id when id == CommandIds.LayoutCards => ViewLayoutMode.Cards,
			var id when id == CommandIds.LayoutGrid => ViewLayoutMode.Grid,
			var id when id == CommandIds.LayoutColumns => ViewLayoutMode.Columns,
			_ => throw new InvalidOperationException($"Unsupported layout command '{commandId}'."),
		};

	private static FolderViewMode ToFolderViewMode(ViewLayoutMode mode) =>
		mode switch
		{
			ViewLayoutMode.Details => FolderViewMode.Details,
			ViewLayoutMode.List => FolderViewMode.List,
			ViewLayoutMode.Cards => FolderViewMode.Cards,
			ViewLayoutMode.Grid => FolderViewMode.Grid,
			ViewLayoutMode.Columns => FolderViewMode.Columns,
			_ => throw new InvalidOperationException($"Unsupported folder layout mode '{mode}'."),
		};
}

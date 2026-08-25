// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ViewSettings;
using Files.ViewModels;

namespace Files.Commands.Handlers;

internal sealed class DisplayCommandHandler(CommandId id) : ICommandHandler
{
	private const string NameParameter = "name";
	private const string DateModifiedParameter = "dateModified";
	private const string DateCreatedParameter = "dateCreated";
	private const string SizeParameter = "size";
	private const string TypeParameter = "type";
	private const string AscendingParameter = "ascending";
	private const string DescendingParameter = "descending";
	private const string NoneParameter = "none";

	public CommandId Id => id;

	public CommandConcurrencyPolicy ConcurrencyPolicy => CommandConcurrencyPolicy.CancelPrevious;

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

		var isChecked = id == CommandIds.ShowHiddenItems
			? browser.ShowHiddenItems
			: id == CommandIds.ShowFileExtensions && browser.ShowFileExtensions;

		return new(true, !browser.IsLoading, isChecked);
	}

	public async ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
	{
		if (context.ActiveFolderBrowser is not { } browser)
		{
			return CommandExecutionResult.Unsupported();
		}

		if (id == CommandIds.ShowHiddenItems)
		{
			await browser.SetShowHiddenItemsAsync(!browser.ShowHiddenItems, cancellationToken).ConfigureAwait(false);
		}
		else if (id == CommandIds.ShowFileExtensions)
		{
			await browser.SetShowFileExtensionsAsync(!browser.ShowFileExtensions, cancellationToken).ConfigureAwait(false);
		}
		else if (context.Parameter is not string parameter)
		{
			return CommandExecutionResult.Unsupported();
		}
		else if (id == CommandIds.SortItems)
		{
			await ApplySortAsync(browser, parameter, cancellationToken).ConfigureAwait(false);
		}
		else if (id == CommandIds.GroupItems)
		{
			await ApplyGroupingAsync(browser, parameter, cancellationToken).ConfigureAwait(false);
		}
		else
		{
			throw new InvalidOperationException($"Unsupported display command '{id}'.");
		}

		return CommandExecutionResult.Succeeded();
	}

	private static ValueTask ApplySortAsync(FolderBrowserViewModel browser, string parameter, CancellationToken cancellationToken)
	{
		var settings = browser.ViewSettings;
		if (TryGetPropertyId(parameter, out var propertyId))
		{
			return browser.SetSortAsync(propertyId, settings.SortDirection, cancellationToken);
		}

		var direction = GetDirection(parameter);

		return browser.SetSortAsync(settings.SortPropertyId ?? BrowseDisplayPropertyIds.Name, direction, cancellationToken);
	}

	private static ValueTask ApplyGroupingAsync(FolderBrowserViewModel browser, string parameter, CancellationToken cancellationToken)
	{
		var settings = browser.ViewSettings;
		if (parameter.Equals(NoneParameter, StringComparison.Ordinal))
		{
			return browser.SetGroupingAsync(null, settings.GroupDirection, cancellationToken);
		}

		if (TryGetPropertyId(parameter, out var propertyId))
		{
			return browser.SetGroupingAsync(propertyId, settings.GroupDirection, cancellationToken);
		}

		var direction = GetDirection(parameter);

		return browser.SetGroupingAsync(settings.GroupPropertyId ?? BrowseDisplayPropertyIds.Name, direction, cancellationToken);
	}

	private static bool TryGetPropertyId(string parameter, out string propertyId)
	{
		propertyId = parameter switch
		{
			NameParameter => BrowseDisplayPropertyIds.Name,
			DateModifiedParameter => BrowseDisplayPropertyIds.DateModified,
			DateCreatedParameter => BrowseDisplayPropertyIds.DateCreated,
			SizeParameter => BrowseDisplayPropertyIds.Size,
			TypeParameter => BrowseDisplayPropertyIds.Type,
			_ => string.Empty,
		};

		return propertyId.Length is not 0;
	}

	private static ViewSortDirection GetDirection(string parameter)
	{
		return parameter switch
		{
			AscendingParameter => ViewSortDirection.Ascending,
			DescendingParameter => ViewSortDirection.Descending,
			_ => throw new ArgumentException($"Unsupported display command parameter '{parameter}'.", nameof(parameter)),
		};
	}
}

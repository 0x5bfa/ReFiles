// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Files.Core.Browsing;
using Files.Core.Storage;
using Files.Core.Windows;

namespace Files.Commands;

public sealed record CommandContext
{
	public RootViewModel Root { get; }
	public object? Parameter { get; }
	public Guid WindowId { get; }
	public TabViewModel? ActiveTab { get; }
	public PaneViewModel? ActivePane { get; }
	public FolderBrowserViewModel? ActiveFolderBrowser { get; }
	public long Generation { get; }
	public BrowseLocation? Location { get; }
	public IReadOnlyList<StorableReference> SelectedReferences { get; }
	public StorableReference? InvokedReference { get; }
	public bool InvokedItemIsFolder { get; }
	public string? Path => Parameter as string;
	public string? Query => Parameter as string;
	public BrowseItemViewModel? InvokedItem { get; }
	public TabViewModel? InvokedTab { get; }
	internal WindowsShellInvocationPoint? InvocationPoint { get; }

	public CommandContext(RootViewModel root, object? parameter = null)
	{
		ArgumentNullException.ThrowIfNull(root);

		Root = root;
		Parameter = parameter;
		WindowId = root.WindowId;
		ActiveTab = root.ActiveTab;
		ActivePane = ActiveTab?.ActivePane;
		ActiveFolderBrowser = ActivePane?.FolderBrowser;
		Generation = ActiveFolderBrowser?.Generation ?? 0;
		Location = ActiveFolderBrowser?.Location;
		SelectedReferences = Array.AsReadOnly(ActiveFolderBrowser?.SelectedItems.Select(static item => item.Reference).ToArray() ?? []);
		InvokedItem = parameter is OpenItemCommandParameter openItem ? openItem.Item : parameter as BrowseItemViewModel;
		InvokedReference = InvokedItem?.Reference;
		InvokedItemIsFolder = InvokedItem?.IsFolder is true;
		InvokedTab = parameter as TabViewModel;
		InvocationPoint = (parameter as OpenItemCommandParameter)?.InvocationPoint;
	}

	internal Guid GetScopeId(CommandExecutionScope scope)
	{
		return scope switch
		{
			CommandExecutionScope.Tab => (InvokedTab ?? ActiveTab)?.Id ?? WindowId,
			CommandExecutionScope.Pane => ActivePane?.Id ?? WindowId,
			_ => WindowId,
		};
	}

	internal bool IsCurrent(CommandExecutionScope scope)
	{
		if (Root.IsDisposed)
		{
			return false;
		}

		return scope switch
		{
			CommandExecutionScope.Tab => (InvokedTab ?? ActiveTab)?.IsDisposed is not true,
			CommandExecutionScope.Pane => ActivePane?.IsDisposed is not true && ActiveTab?.IsDisposed is not true
				&& (ActiveFolderBrowser is null || (!ActiveFolderBrowser.IsDisposed && ActiveFolderBrowser.Generation == Generation)),
			_ => true,
		};
	}
}

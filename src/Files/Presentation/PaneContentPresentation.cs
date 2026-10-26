// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Files.Presentation;

internal sealed partial class PaneContentPresentation : IAsyncDisposable
{
	private readonly Func<UIElement> _viewFactory;
	private readonly string _title;

	internal object Content { get; }
	internal FolderBrowserViewModel? FolderBrowser { get; }
	internal PreviewPaneViewModel? Preview { get; }
	internal string Title => FolderBrowser?.LocationDisplayName ?? _title;
	internal ImageSource? Icon => FolderBrowser?.LocationIcon;
	internal IconSource? IconSource { get; }
	internal bool HasPaneChrome => FolderBrowser is not null;

	internal PaneContentPresentation(object content, Func<UIElement> viewFactory, string title = "", IconSource? iconSource = null,
		FolderBrowserViewModel? folderBrowser = null, PreviewPaneViewModel? preview = null)
	{
		Content = content;
		_viewFactory = viewFactory;
		_title = title;
		IconSource = iconSource;
		FolderBrowser = folderBrowser;
		Preview = preview;
	}

	internal UIElement CreateView()
	{
		return _viewFactory();
	}

	public async ValueTask DisposeAsync()
	{
		Preview?.Dispose();
		if (FolderBrowser is not null)
		{
			await FolderBrowser.DisposeAsync().ConfigureAwait(false);
		}
	}
}

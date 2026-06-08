// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Controls;

using Files.Localization;

namespace Files.Views;

public sealed partial class WebView : UserControl
{
	public WebView()
	{
		InitializeComponent();
	}

	public string Text => Strings.WebView.GetLocalized();
}

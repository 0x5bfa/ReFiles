// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Controls;

using Files.Localization;

namespace Files.Views;

public sealed partial class InfoPane : UserControl
{
	public string Text => Strings.InfoPane.GetLocalized();

	public InfoPane()
	{
		InitializeComponent();
	}
}

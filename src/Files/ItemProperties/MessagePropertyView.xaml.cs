// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Controls;

namespace Files.ItemProperties;

public sealed partial class MessagePropertyView : UserControl
{
	internal string Message { get; }

	internal MessagePropertyView(string message)
	{
		Message = message;
		InitializeComponent();
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Controls;

namespace Files.Views;

internal sealed partial class OperationErrorDialog : ContentDialog
{
	internal string Message { get; }

	internal OperationErrorDialog(string message)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(message);

		Message = message;
		InitializeComponent();
	}
}

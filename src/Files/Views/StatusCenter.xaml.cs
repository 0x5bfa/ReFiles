// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.Views;

public sealed partial class StatusCenter : UserControl
{
	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.Register(nameof(ViewModel), typeof(StatusCenterViewModel), typeof(StatusCenter), new PropertyMetadata(null));

	public StatusCenterViewModel? ViewModel
	{
		get => (StatusCenterViewModel?)GetValue(ViewModelProperty);
		set => SetValue(ViewModelProperty, value);
	}

	public StatusCenter()
	{
		InitializeComponent();
	}

	private void ClearCompletedButton_Click(object sender, RoutedEventArgs e)
	{
		ViewModel?.ClearCompleted();
	}

	private void CancelButton_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: Guid operationId })
		{
			ViewModel?.Cancel(operationId);
		}
	}

	private void ExpandButton_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: Guid operationId })
		{
			ViewModel?.ToggleExpanded(operationId);
		}
	}

	private void PauseButton_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: Guid operationId })
		{
			ViewModel?.TogglePaused(operationId);
		}
	}

	private void RemoveButton_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: Guid operationId })
		{
			ViewModel?.Remove(operationId);
		}
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.Views;

public sealed partial class DetailsColumnTemplateSelector : DataTemplateSelector
{
	public DataTemplate? PrimaryColumnTemplate { get; set; }

	public DataTemplate? TextColumnTemplate { get; set; }

	protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
	{
		if (item is not DetailsColumnViewModel column)
		{
			return base.SelectTemplateCore(item, container);
		}

		return column.IsPrimary ? PrimaryColumnTemplate : TextColumnTemplate;
	}
}

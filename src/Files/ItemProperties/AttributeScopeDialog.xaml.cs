// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Localization;
using Microsoft.UI.Xaml.Controls;

namespace Files.ItemProperties;

internal sealed partial class AttributeScopeDialog : ContentDialog
{
	private readonly ItemPropertiesViewModel _viewModel;

	internal bool ApplyToContents => IncludeContentsRadioButton.IsChecked is true;

	internal string CancelLabel => Strings.Cancel.GetLocalized();

	internal string DialogTitle => Strings.ConfirmAttributeChanges.GetLocalized();

	internal string IncludeContentsLabel => _viewModel.IsDrive
		? Strings.ApplyChangesToDriveContents.GetLocalized()
		: _viewModel.IsSingleFolder ? Strings.ApplyChangesToFolderContents.GetLocalized() : Strings.ApplyChangesToSelectionContents.GetLocalized();

	internal string Intro => Strings.AttributeChangesIntro.GetLocalized();

	internal string OkLabel => Strings.Ok.GetLocalized();

	internal string Question => _viewModel.IsDrive
		? Strings.AttributeChangesDriveQuestion.GetLocalized()
		: _viewModel.IsSingleFolder ? Strings.AttributeChangesFolderQuestion.GetLocalized() : Strings.AttributeChangesSelectionQuestion.GetLocalized();

	internal string SelectedItemsOnlyLabel => _viewModel.IsDrive
		? Strings.ApplyChangesToDriveOnly.GetLocalized()
		: _viewModel.IsSingleFolder ? Strings.ApplyChangesToFolderOnly.GetLocalized() : Strings.ApplyChangesToSelectionOnly.GetLocalized();

	internal AttributeScopeDialog(ItemPropertiesViewModel viewModel)
	{
		_viewModel = viewModel;
		InitializeComponent();
	}
}

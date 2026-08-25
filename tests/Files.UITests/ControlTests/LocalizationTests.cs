// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Localization;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;

namespace Files.UITests.ControlTests;

[TestClass]
public sealed class LocalizationTests
{
	/// <summary>
	/// Verifies that the localization markup extension resolves an application string resource.
	/// </summary>
	[UITestMethod]
	public void MarkupExtensionResolvesStringResource()
	{
		const string xaml = """
			<TextBlock
				xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
				xmlns:localization="using:Files.Localization"
				Text="{localization:Localized ResourceKey=FolderEmpty}" />
			""";

		var textBlock = Assert.IsInstanceOfType<TextBlock>(XamlReader.Load(xaml));
		Assert.AreEqual(LocalizationExtensions.GetLocalized(Strings.FolderEmpty), textBlock.Text);
	}
}

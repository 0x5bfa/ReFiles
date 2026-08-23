// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.Storage.Windows;
using Files.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.ItemProperties;

public sealed partial class DigitalSignaturesPropertyView : UserControl
{
	internal IReadOnlyList<SignatureDisplayRow> CatalogSignatures { get; }

	internal IReadOnlyList<SignatureDisplayRow> EmbeddedSignatures { get; }

	internal string CatalogEmptyLabel => Strings.NoSignatures.GetLocalized();

	internal Visibility CatalogEmptyVisibility => CatalogSignatures.Count is 0 ? Visibility.Visible : Visibility.Collapsed;

	internal string CatalogNameLabel => Strings.CatalogName.GetLocalized();

	internal string CatalogSignaturesLabel => Strings.CatalogSignatures.GetLocalized();

	internal string DetailsLabel => Strings.Details.GetLocalized();

	internal string DigestAlgorithmLabel => Strings.DigestAlgorithm.GetLocalized();

	internal string EmbeddedEmptyLabel => Strings.NoSignatures.GetLocalized();

	internal Visibility EmbeddedEmptyVisibility => EmbeddedSignatures.Count is 0 ? Visibility.Visible : Visibility.Collapsed;

	internal string EmbeddedSignaturesLabel => Strings.EmbeddedSignatures.GetLocalized();

	internal string SignerNameLabel => Strings.SignerName.GetLocalized();

	internal string TimestampLabel => Strings.Timestamp.GetLocalized();

	internal DigitalSignaturesPropertyView(IReadOnlyList<WindowsShellDigitalSignature> embeddedSignatures, IReadOnlyList<WindowsShellDigitalSignature> catalogSignatures)
	{
		EmbeddedSignatures = embeddedSignatures.Select(static signature => new SignatureDisplayRow(signature.Signer, signature.DigestAlgorithm, signature.Timestamp)).ToArray();
		CatalogSignatures = catalogSignatures.Select(static signature => new SignatureDisplayRow(signature.Signer, signature.DigestAlgorithm, Path.GetFileName(signature.CatalogPath))).ToArray();
		InitializeComponent();
	}
}

public sealed class SignatureDisplayRow
{
	public string Signer { get; }

	public string DigestAlgorithm { get; }

	public string ThirdColumn { get; }

	internal SignatureDisplayRow(string signer, string digestAlgorithm, string thirdColumn)
	{
		Signer = signer;
		DigestAlgorithm = digestAlgorithm;
		ThirdColumn = thirdColumn;
	}
}

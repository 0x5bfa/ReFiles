// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Describes an embedded or catalog Authenticode signature.
/// </summary>
public sealed class WindowsShellDigitalSignature
{
	/// <summary>Gets the signer certificate subject.</summary>
	public string Signer { get; }

	/// <summary>Gets the message digest algorithm.</summary>
	public string DigestAlgorithm { get; }

	/// <summary>Gets the signature timestamp text when available.</summary>
	public string Timestamp { get; }

	/// <summary>Gets the containing catalog path for a catalog signature.</summary>
	public string CatalogPath { get; }

	internal WindowsShellDigitalSignature(string signer, string digestAlgorithm, string timestamp, string catalogPath)
	{
		Signer = signer;
		DigestAlgorithm = digestAlgorithm;
		Timestamp = timestamp;
		CatalogPath = catalogPath;
	}
}

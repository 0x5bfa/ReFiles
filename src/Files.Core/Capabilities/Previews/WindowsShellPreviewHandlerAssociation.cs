// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Files.Core.Capabilities.Previews;

internal delegate HRESULT WindowsPreviewHandlerAssociationQuery(string normalizedExtension, Span<char> buffer, ref uint characterCount);

/// <summary>Reads Windows Shell preview handler associations.</summary>
[SupportedOSPlatform("windows5.0")]
public sealed class WindowsShellPreviewHandlerAssociation : IWindowsPreviewHandlerAssociation
{
	private const string PreviewHandlerCategory = "{8895B1C6-B41F-4C1C-A562-0D564250836F}";
	internal const uint MaximumAssociationCharacterCount = 128;

	private readonly WindowsPreviewHandlerAssociationQuery _queryAssociation;

	/// <summary>Initializes a Windows Shell preview handler association reader.</summary>
	public WindowsShellPreviewHandlerAssociation()
		: this(QueryAssociation)
	{
	}

	internal WindowsShellPreviewHandlerAssociation(WindowsPreviewHandlerAssociationQuery queryAssociation)
	{
		ArgumentNullException.ThrowIfNull(queryAssociation);

		_queryAssociation = queryAssociation;
	}

	/// <summary>Queries the preview handler associated with an extension.</summary>
	/// <param name="normalizedExtension">The normalized extension.</param>
	/// <returns>The handler CLSID string, or <see langword="null"/> when none is registered.</returns>
	public string? QueryPreviewHandler(string normalizedExtension)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(normalizedExtension);

		uint characterCount = 0;
		var firstResult = _queryAssociation(normalizedExtension, Span<char>.Empty, ref characterCount);

		if (characterCount is 0 || characterCount > MaximumAssociationCharacterCount || firstResult != HRESULT.S_FALSE && firstResult != HRESULT.S_OK)
		{
			return null;
		}

		var buffer = new char[(int)characterCount];
		var secondResult = _queryAssociation(normalizedExtension, buffer, ref characterCount);

		if (secondResult != HRESULT.S_OK || characterCount is 0 || characterCount > buffer.Length)
		{
			return null;
		}

		var value = buffer.AsSpan(0, (int)characterCount);
		var terminator = value.IndexOf('\0');

		return (terminator >= 0 ? value[..terminator] : value).ToString();
	}

	private static unsafe HRESULT QueryAssociation(string normalizedExtension, Span<char> buffer, ref uint characterCount)
	{
		return PInvoke.AssocQueryString(ASSOCF.ASSOCF_NONE, ASSOCSTR.ASSOCSTR_SHELLEXTENSION, normalizedExtension, PreviewHandlerCategory, buffer, ref characterCount);
	}
}

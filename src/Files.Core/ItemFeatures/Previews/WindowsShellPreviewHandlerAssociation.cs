// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Files.Core.ItemFeatures.Previews;

[SupportedOSPlatform("windows5.0")]
public sealed class WindowsShellPreviewHandlerAssociation : IWindowsPreviewHandlerAssociation
{
	private const string PreviewHandlerCategory =
		"{8895B1C6-B41F-4C1C-A562-0D564250836F}";

	public unsafe string? QueryPreviewHandler(string normalizedExtension)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(normalizedExtension);

		uint characterCount = 0;
		var firstResult = PInvoke.AssocQueryString(
			ASSOCF.ASSOCF_NONE,
			ASSOCSTR.ASSOCSTR_SHELLEXTENSION,
			normalizedExtension,
			PreviewHandlerCategory,
			Span<char>.Empty,
			ref characterCount);

		if (characterCount is 0 || firstResult.Failed && firstResult != HRESULT.S_FALSE)
		{
			return null;
		}

		if (characterCount > int.MaxValue)
		{
			throw new InvalidOperationException(
				"The Shell association returned an invalid buffer size.");
		}

		Span<char> buffer = stackalloc char[(int)characterCount];
		var secondResult = PInvoke.AssocQueryString(
			ASSOCF.ASSOCF_NONE,
			ASSOCSTR.ASSOCSTR_SHELLEXTENSION,
			normalizedExtension,
			PreviewHandlerCategory,
			buffer,
			ref characterCount);

		if (secondResult.Failed)
		{
			return null;
		}

		var value = buffer[..(int)Math.Min(characterCount, (uint)buffer.Length)];
		var terminator = value.IndexOf('\0');
		return (terminator >= 0 ? value[..terminator] : value).ToString();
	}
}

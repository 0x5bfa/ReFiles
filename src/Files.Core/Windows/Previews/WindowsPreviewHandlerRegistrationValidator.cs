// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.Versioning;
using Files.Core.Capabilities;

namespace Files.Core.Windows;

[SupportedOSPlatform("windows5.0")]
internal sealed class WindowsPreviewHandlerRegistrationValidator : IWindowsPreviewHandlerRegistrationValidator
{
	private readonly IWindowsPreviewHandlerAssociation _association;
	private readonly IWindowsPreviewHandlerRegistrationAllowlist _registrationAllowlist;

	public WindowsPreviewHandlerRegistrationValidator()
		: this(new WindowsShellPreviewHandlerAssociation(), WindowsPreviewHandlerRegistrationAllowlist.Instance)
	{
	}

	internal WindowsPreviewHandlerRegistrationValidator(IWindowsPreviewHandlerAssociation association, IWindowsPreviewHandlerRegistrationAllowlist registrationAllowlist)
	{
		ArgumentNullException.ThrowIfNull(association);
		ArgumentNullException.ThrowIfNull(registrationAllowlist);

		_association = association;
		_registrationAllowlist = registrationAllowlist;
	}

	public bool IsCurrentHandler(ItemContext context, Guid handlerClsid)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (handlerClsid == Guid.Empty)
		{
			return false;
		}

		var extension = WindowsPreviewHandlerResolver.GetNormalizedExtension(context);
		var rawClsid = extension is null ? null : _association.QueryPreviewHandler(extension);

		return !string.IsNullOrWhiteSpace(rawClsid) && Guid.TryParse(rawClsid.Trim(), out var associatedClsid) && associatedClsid == handlerClsid && _registrationAllowlist.IsRegistered(handlerClsid);
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.IO;
using System.Runtime.Versioning;
using System.Security;
using Files.Core.Capabilities;
using Microsoft.Win32;
using OwlCore.Storage;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Files.Core.Windows;

/// <summary>Resolves and caches Windows Shell preview handler registrations.</summary>
public sealed class WindowsPreviewHandlerResolver : IWindowsPreviewHandlerResolver
{
	private readonly IWindowsPreviewHandlerAssociation _association;
	private readonly IWindowsPreviewHandlerRegistrationAllowlist _registrationAllowlist;
	private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
	private readonly Lock _cacheLock = new();

	/// <summary>Initializes a resolver that uses Windows Shell associations and the per-user and machine-wide registration allowlist.</summary>
	[SupportedOSPlatform("windows5.0")]
	public WindowsPreviewHandlerResolver()
		: this(new WindowsShellPreviewHandlerAssociation(), WindowsPreviewHandlerRegistrationAllowlist.Instance)
	{
	}

	/// <summary>Initializes a resolver that trusts a caller-provided association lookup.</summary>
	/// <remarks>Use this overload for controlled custom associations or tests. Use the parameterless constructor for production Windows Shell registration lookup.</remarks>
	/// <param name="association">The trusted association lookup.</param>
	public WindowsPreviewHandlerResolver(IWindowsPreviewHandlerAssociation association)
		: this(association, TrustedAssociationRegistrationAllowlist.Instance)
	{
	}

	/// <summary>Initializes a preview handler resolver with an explicit registration allowlist.</summary>
	/// <param name="association">The Shell association lookup.</param>
	/// <param name="registrationAllowlist">The allowlist of handlers registered for use by the Shell preview host.</param>
	public WindowsPreviewHandlerResolver(IWindowsPreviewHandlerAssociation association, IWindowsPreviewHandlerRegistrationAllowlist registrationAllowlist)
	{
		ArgumentNullException.ThrowIfNull(association);
		ArgumentNullException.ThrowIfNull(registrationAllowlist);

		_association = association;
		_registrationAllowlist = registrationAllowlist;
	}

	/// <summary>Resolves the handler registered for an item.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The handler CLSID, or <see langword="null"/> when none is registered.</returns>
	public ValueTask<Guid?> ResolveAsync(ItemContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);
		cancellationToken.ThrowIfCancellationRequested();

		var extension = GetNormalizedExtension(context);
		if (extension is null)
		{
			return ValueTask.FromResult<Guid?>(null);
		}

		lock (_cacheLock)
		{
			if (_cache.TryGetValue(extension, out var cached))
			{
				return ValueTask.FromResult(cached.Clsid);
			}
		}

		cancellationToken.ThrowIfCancellationRequested();

		var rawClsid = _association.QueryPreviewHandler(extension);
		Guid? clsid = null;
		if (!string.IsNullOrWhiteSpace(rawClsid) && Guid.TryParse(rawClsid.Trim(), out var parsed) && parsed != Guid.Empty && _registrationAllowlist.IsRegistered(parsed))
		{
			clsid = parsed;
		}

		lock (_cacheLock)
		{
			_cache[extension] = new CacheEntry(clsid);
		}

		return ValueTask.FromResult(clsid);
	}

	/// <summary>Clears the cached extension associations.</summary>
	public void ClearCache()
	{
		lock (_cacheLock)
		{
			_cache.Clear();
		}
	}

	/// <summary>Normalizes a file extension for Shell association lookup.</summary>
	/// <param name="extension">The extension to normalize.</param>
	/// <returns>The normalized extension, or <see langword="null"/> when invalid.</returns>
	public static string? NormalizeExtension(string? extension)
	{
		if (string.IsNullOrWhiteSpace(extension)
			|| extension.Length < 2
			|| extension[0] != '.'
			|| extension.Any(char.IsWhiteSpace)
			|| extension.IndexOf('.', 1) >= 0
			|| extension.Contains('/')
			|| extension.Contains('\\'))
		{
			return null;
		}

		return extension.ToUpperInvariant();
	}

	internal static string? GetNormalizedExtension(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (context.CoreModel is not IWindowsStorable windowsItem || context.CoreModel is not IFile file)
		{
			return null;
		}

		return NormalizeExtension(Path.GetExtension(windowsItem.FileSystemPath ?? file.Name)) ?? NormalizeExtension(Path.GetExtension(file.Name));
	}

	private sealed record CacheEntry(Guid? Clsid);

	private sealed class TrustedAssociationRegistrationAllowlist : IWindowsPreviewHandlerRegistrationAllowlist
	{
		public static TrustedAssociationRegistrationAllowlist Instance { get; } = new();

		public bool IsRegistered(Guid handlerClsid) => true;
	}
}

/// <summary>Reads Windows Shell preview handler associations.</summary>
[SupportedOSPlatform("windows5.0")]
public sealed class WindowsShellPreviewHandlerAssociation : IWindowsPreviewHandlerAssociation
{
	private const string PreviewHandlerCategory = "{8895B1C6-B41F-4C1C-A562-0D564250836F}";
	internal const uint MaximumAssociationCharacterCount = 128;

	private readonly WindowsPreviewHandlerAssociationQuery _queryAssociation;

	/// <summary>Initializes a Windows Shell preview handler association reader.</summary>
	public WindowsShellPreviewHandlerAssociation() : this(QueryAssociation)
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

/// <summary>Checks the per-user and machine-wide Windows Shell preview handler registrations.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPreviewHandlerRegistrationAllowlist : IWindowsPreviewHandlerRegistrationAllowlist
{
	private const string PreviewHandlersRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\PreviewHandlers";

	private readonly Func<RegistryHive, string, bool> _registrationLookup;

	/// <summary>Gets the shared registration allowlist.</summary>
	public static WindowsPreviewHandlerRegistrationAllowlist Instance { get; } = new();

	/// <summary>Initializes a Windows preview handler registration allowlist.</summary>
	public WindowsPreviewHandlerRegistrationAllowlist() : this(IsRegistryValueDefined)
	{
	}

	internal WindowsPreviewHandlerRegistrationAllowlist(Func<RegistryHive, string, bool> registrationLookup)
	{
		ArgumentNullException.ThrowIfNull(registrationLookup);

		_registrationLookup = registrationLookup;
	}

	/// <inheritdoc />
	public bool IsRegistered(Guid handlerClsid)
	{
		if (handlerClsid == Guid.Empty)
		{
			throw new ArgumentException("A preview handler CLSID is required.", nameof(handlerClsid));
		}

		var valueName = handlerClsid.ToString("B");

		return IsRegistered(RegistryHive.CurrentUser, valueName) || IsRegistered(RegistryHive.LocalMachine, valueName);
	}

	private bool IsRegistered(RegistryHive hive, string valueName)
	{
		try
		{
			return _registrationLookup(hive, valueName);
		}
		catch (Exception exception) when (exception is IOException or SecurityException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static bool IsRegistryValueDefined(RegistryHive hive, string valueName)
	{
		using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
		using var previewHandlersKey = baseKey.OpenSubKey(PreviewHandlersRegistryPath);
		if (previewHandlersKey is null)
		{
			return false;
		}

		return IsRegistrationValueKind(previewHandlersKey.GetValueKind(valueName));
	}

	internal static bool IsRegistrationValueKind(RegistryValueKind valueKind) => valueKind is RegistryValueKind.String;
}

[SupportedOSPlatform("windows5.0")]
internal sealed class WindowsPreviewHandlerRegistrationValidator : IWindowsPreviewHandlerRegistrationValidator
{
	private readonly IWindowsPreviewHandlerAssociation _association;
	private readonly IWindowsPreviewHandlerRegistrationAllowlist _registrationAllowlist;

	public WindowsPreviewHandlerRegistrationValidator() : this(new WindowsShellPreviewHandlerAssociation(), WindowsPreviewHandlerRegistrationAllowlist.Instance)
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

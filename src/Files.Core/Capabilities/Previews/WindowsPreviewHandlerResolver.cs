// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.Versioning;
using Files.Core.Capabilities;
using Files.Core.Storage.Windows;
using OwlCore.Storage;

namespace Files.Core.Capabilities.Previews;

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

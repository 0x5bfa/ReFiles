// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.Capabilities;
using Files.Core.Storage.Windows;
using OwlCore.Storage;

namespace Files.Core.Capabilities.Previews;

/// <summary>Resolves and caches Windows Shell preview handler registrations.</summary>
public sealed class WindowsPreviewHandlerResolver : IWindowsPreviewHandlerResolver
{
	private readonly IWindowsPreviewHandlerAssociation _association;
	private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
	private readonly Lock _cacheLock = new();

	/// <summary>Initializes a preview handler resolver.</summary>
	/// <param name="association">The Shell association lookup.</param>
	public WindowsPreviewHandlerResolver(IWindowsPreviewHandlerAssociation association)
	{
		ArgumentNullException.ThrowIfNull(association);

		_association = association;
	}

	/// <summary>Resolves the handler registered for an item.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The handler CLSID, or <see langword="null"/> when none is registered.</returns>
	public ValueTask<Guid?> ResolveAsync(ItemContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);
		cancellationToken.ThrowIfCancellationRequested();

		if (context.CoreModel is not IWindowsStorable || context.CoreModel is not IFile file)
		{
			return ValueTask.FromResult<Guid?>(null);
		}

		var extension = NormalizeExtension(Path.GetExtension(((IWindowsStorable)context.CoreModel).FileSystemPath ?? file.Name))
			?? NormalizeExtension(Path.GetExtension(file.Name));
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
		if (!string.IsNullOrWhiteSpace(rawClsid) && Guid.TryParse(rawClsid.Trim(), out var parsed) && parsed != Guid.Empty)
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

	private sealed record CacheEntry(Guid? Clsid);
}

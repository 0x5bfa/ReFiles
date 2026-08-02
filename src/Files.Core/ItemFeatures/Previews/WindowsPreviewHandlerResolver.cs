// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.ItemFeatures;
using Files.Core.Storage.Windows;
using OwlCore.Storage;

namespace Files.Core.ItemFeatures.Previews;

public sealed class WindowsPreviewHandlerResolver : IWindowsPreviewHandlerResolver
{
	private readonly IWindowsPreviewHandlerAssociation _association;
	private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
	private readonly Lock _cacheLock = new();

	public WindowsPreviewHandlerResolver(IWindowsPreviewHandlerAssociation association)
	{
		ArgumentNullException.ThrowIfNull(association);

		_association = association;
	}

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

	public void ClearCache()
	{
		lock (_cacheLock)
		{
			_cache.Clear();
		}
	}

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

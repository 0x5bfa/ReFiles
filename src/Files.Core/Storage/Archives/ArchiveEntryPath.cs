// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

/// <summary>
/// Normalizes untrusted archive entry names into root-relative logical paths.
/// </summary>
public static class ArchiveEntryPath
{
	public static string Normalize(string? path)
	{
		if (!TryNormalize(path, out var normalized))
		{
			throw new ArgumentException("The archive entry path must be relative and cannot traverse above the archive root.", nameof(path));
		}

		return normalized;
	}

	public static bool TryNormalize(string? path, out string normalized)
	{
		normalized = string.Empty;
		if (string.IsNullOrWhiteSpace(path))
		{
			return true;
		}

		if (path[0] is '/' or '\\'
			|| path.IndexOf('\0') >= 0)
		{
			return false;
		}

		var segments = path
			.Replace('\\', '/')
			.Split('/', StringSplitOptions.RemoveEmptyEntries);
		if (segments.Length > 0
			&& segments[0].Length >= 2
			&& char.IsAsciiLetter(segments[0][0])
			&& segments[0][1] is ':')
		{
			return false;
		}

		var acceptedSegments = new List<string>(segments.Length);

		foreach (var segment in segments)
		{
			if (segment is ".")
			{
				continue;
			}

			if (segment is "..")
			{
				return false;
			}

			acceptedSegments.Add(segment);
		}

		normalized = string.Join('/', acceptedSegments);
		return true;
	}

	public static string Combine(string parent, string child)
	{
		var normalizedParent = Normalize(parent);
		var normalizedChild = Normalize(child);

		return string.IsNullOrEmpty(normalizedParent)
			? normalizedChild
			: string.IsNullOrEmpty(normalizedChild)
				? normalizedParent
				: $"{normalizedParent}/{normalizedChild}";
	}

	public static string GetName(string path)
	{
		var normalized = Normalize(path);
		var separatorIndex = normalized.LastIndexOf('/');
		return separatorIndex < 0
			? normalized
			: normalized[(separatorIndex + 1)..];
	}

	public static string GetParent(string path)
	{
		var normalized = Normalize(path);
		var separatorIndex = normalized.LastIndexOf('/');
		return separatorIndex < 0
			? string.Empty
			: normalized[..separatorIndex];
	}
}

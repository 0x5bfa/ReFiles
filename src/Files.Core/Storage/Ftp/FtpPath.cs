// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Represents one normalized absolute path in an FTP namespace.
/// </summary>
public sealed record FtpPath
{
	private FtpPath(string value)
	{
		Value = value;
	}

	public static FtpPath Root { get; } = new("/");

	public string Value { get; }

	public bool IsRoot => Value.Length is 1;

	public string Name =>
		IsRoot ? string.Empty : Value[(Value.LastIndexOf('/') + 1)..];

	public FtpPath? Parent
	{
		get
		{
			if (IsRoot)
			{
				return null;
			}

			var separatorIndex = Value.LastIndexOf('/');
			return separatorIndex is 0
				? Root
				: new FtpPath(Value[..separatorIndex]);
		}
	}

	public static FtpPath Parse(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);

		var normalized = value.Replace('\\', '/');
		if (normalized[0] is not '/')
		{
			normalized = $"/{normalized}";
		}

		var segments = new List<string>();
		foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
		{
			if (segment is ".")
			{
				continue;
			}

			if (segment is "..")
			{
				if (segments.Count is 0)
				{
					throw new ArgumentException("An FTP path cannot escape its root.", nameof(value));
				}

				segments.RemoveAt(segments.Count - 1);
				continue;
			}

			ValidateSegment(segment, nameof(value));
			segments.Add(segment);
		}

		return segments.Count is 0
			? Root
			: new FtpPath($"/{string.Join('/', segments)}");
	}

	public static FtpPath ParseEscapedUriPath(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);

		var rawSegments = value
			.Replace('\\', '/')
			.Split('/', StringSplitOptions.RemoveEmptyEntries);
		var decodedSegments = rawSegments
			.Select(Uri.UnescapeDataString)
			.ToArray();
		foreach (var segment in decodedSegments)
		{
			ValidateName(segment);
		}

		return Parse($"/{string.Join('/', decodedSegments)}");
	}

	public FtpPath Combine(string childName)
	{
		ValidateName(childName);
		return new FtpPath(IsRoot ? $"/{childName}" : $"{Value}/{childName}");
	}

	public bool IsWithin(FtpPath root, StringComparer comparer)
	{
		ArgumentNullException.ThrowIfNull(root);
		ArgumentNullException.ThrowIfNull(comparer);

		if (root.IsRoot)
		{
			return true;
		}

		if (comparer.Equals(Value, root.Value))
		{
			return true;
		}

		return Value.Length > root.Value.Length
			&& Value[root.Value.Length] is '/'
			&& comparer.Equals(Value[..root.Value.Length], root.Value);
	}

	public string ToEscapedUriPath()
	{
		if (IsRoot)
		{
			return "/";
		}

		return $"/{string.Join(
			'/',
			Value
				.Split('/', StringSplitOptions.RemoveEmptyEntries)
				.Select(Uri.EscapeDataString))}";
	}

	public static void ValidateName(string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		if (name is "." or ".."
			|| name.Contains('/')
			|| name.Contains('\\')
			|| name.Any(char.IsControl))
		{
			throw new ArgumentException("An FTP item name must be one path segment.", nameof(name));
		}
	}

	public override string ToString() => Value;

	private static void ValidateSegment(string segment, string parameterName)
	{
		if (segment.Any(char.IsControl))
		{
			throw new ArgumentException("An FTP path cannot contain control characters.", parameterName);
		}
	}
}

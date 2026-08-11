// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Represents one normalized absolute path in an FTP namespace.
/// </summary>
public sealed record FtpPath
{
	/// <summary>Gets the root FTP path.</summary>
	public static FtpPath Root { get; } = new("/");

	/// <summary>Gets the normalized absolute path value.</summary>
	public string Value { get; }

	/// <summary>Gets a value indicating whether this path is the root.</summary>
	public bool IsRoot => Value.Length is 1;

	/// <summary>Gets the final path segment.</summary>
	public string Name =>
		IsRoot ? string.Empty : Value[(Value.LastIndexOf('/') + 1)..];

	/// <summary>Gets the parent path, or <see langword="null"/> for the root.</summary>
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

	private FtpPath(string value)
	{
		Value = value;
	}

	/// <summary>Parses and normalizes an FTP path.</summary>
	/// <param name="value">The path value.</param>
	/// <returns>The normalized FTP path.</returns>
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

	/// <summary>Parses an escaped URI path into an FTP path.</summary>
	/// <param name="value">The escaped path value.</param>
	/// <returns>The normalized FTP path.</returns>
	public static FtpPath ParseEscapedUriPath(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);

		var rawSegments = value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
		var decodedSegments = rawSegments.Select(Uri.UnescapeDataString).ToArray();
		foreach (var segment in decodedSegments)
		{
			ValidateName(segment);
		}

		return Parse($"/{string.Join('/', decodedSegments)}");
	}

	/// <summary>Combines this path with a child name.</summary>
	/// <param name="childName">The child name.</param>
	/// <returns>The combined path.</returns>
	public FtpPath Combine(string childName)
	{
		ValidateName(childName);

		return new FtpPath(IsRoot ? $"/{childName}" : $"{Value}/{childName}");
	}

	/// <summary>Determines whether this path is within a root path.</summary>
	/// <param name="root">The root path.</param>
	/// <param name="comparer">The comparer used for path equality.</param>
	/// <returns><see langword="true"/> when this path is within the root.</returns>
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

	/// <summary>Converts the path to an escaped URI path.</summary>
	/// <returns>The escaped URI path.</returns>
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

	/// <summary>Validates an FTP path segment name.</summary>
	/// <param name="name">The segment name.</param>
	public static void ValidateName(string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		if (name is "." or ".." || name.Contains('/') || name.Contains('\\') || name.Any(char.IsControl))
		{
			throw new ArgumentException("An FTP item name must be one path segment.", nameof(name));
		}
	}

	/// <summary>Returns the normalized path value.</summary>
	/// <returns>The normalized path.</returns>
	public override string ToString() => Value;

	private static void ValidateSegment(string segment, string parameterName)
	{
		if (segment.Any(char.IsControl))
		{
			throw new ArgumentException("An FTP path cannot contain control characters.", parameterName);
		}
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Storage.Archives;
using Files.Core.Storage.Windows;
using OwlCore.Storage;

namespace Files.Core.ItemFeatures.Archives;

public sealed class ArchiveSourceFactory
	: IItemFeatureFactory<IArchiveSource>
{
	private static readonly string[] DefaultExtensions =
	[
		".7z",
		".gz",
		".jar",
		".lzh",
		".mrpack",
		".rar",
		".tar",
		".zip",
	];

	private readonly IReadOnlyList<string> extensions;

	public ArchiveSourceFactory(
		IEnumerable<string>? extensions = null)
	{
		var extensionArray = (extensions ?? DefaultExtensions)
			.Select(NormalizeExtension)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (extensionArray.Length is 0)
		{
			throw new ArgumentException(
				"At least one archive extension is required.",
				nameof(extensions));
		}

		this.extensions = Array.AsReadOnly(extensionArray);
	}

	public IArchiveSource? Create(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		// SevenZip archive entries belong to a scoped mount that is not
		// registered in FilesDataRoot. Nested archives require an explicit
		// mount-chain contract rather than a reference that becomes stale
		// when the containing browse context is replaced.
		if (context.CoreModel is IArchiveEntry
			|| context.CoreModel is IArchiveSource)
		{
			return null;
		}

		var isArchiveFile = context.CoreModel is IFile;
		var isShellArchiveFolder =
			context.Source is WindowsStorageSource
			&& context.CoreModel is IFolder
			&& context.CoreModel is IWindowsStorable
			{
				IsStream: true,
			};
		if (!isArchiveFile && !isShellArchiveFolder)
		{
			return null;
		}

		var extensionSource = context.CoreModel
			is IWindowsStorable windowsStorable
				? windowsStorable.FileSystemPath
					?? windowsStorable.ParsingName
				: context.CoreModel.Name;
		return extensions.Any(
			extension => extensionSource.EndsWith(
				extension,
				StringComparison.OrdinalIgnoreCase))
			? new ArchiveSource(context.Reference)
			: null;
	}

	private static string NormalizeExtension(string extension)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(extension);
		var trimmedExtension = extension.Trim();
		return trimmedExtension[0] is '.'
			? trimmedExtension
			: $".{trimmedExtension}";
	}

	private sealed record ArchiveSource(
		StorableReference Archive)
		: IArchiveSource;
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Capabilities.Content;
using Files.Core.Storage.Archives;
using Files.Core.Windows;
using Files.Core.Models;
using Files.Core.Storage.Archives.SevenZip;

namespace Files.Core.Capabilities.Archives;

/// <summary>Creates archive capabilities for supported storage items.</summary>
public sealed class ArchiveSourceFactory
	: ICapabilityFactory<IArchiveSource>
{
	private static readonly string[] _defaultExtensions =
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

	private readonly IReadOnlyList<string> _extensions;
	private readonly ArchiveBackendSelector _readers;
	private readonly IArchiveCredentialResolver? _credentials;

	/// <summary>Initializes an archive source factory.</summary>
	/// <param name="extensions">The optional supported extension list.</param>
	/// <param name="readers">The optional configured archive readers.</param>
	/// <param name="credentials">The optional credential resolver.</param>
	public ArchiveSourceFactory(IEnumerable<string>? extensions = null, ArchiveBackendSelector? readers = null, IArchiveCredentialResolver? credentials = null)
	{
		var extensionArray = (extensions ?? _defaultExtensions).Select(NormalizeExtension).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		if (extensionArray.Length is 0)
		{
			throw new ArgumentException("At least one archive extension is required.", nameof(extensions));
		}

		_extensions = Array.AsReadOnly(extensionArray);
		_readers = readers ?? new ArchiveBackendSelector([new SevenZipArchiveBackend()]);
		_credentials = credentials;
	}

	/// <inheritdoc />
	public IArchiveSource? Create(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		var isArchiveFile = context.CoreModel is IFile;
		var isShellArchiveFolder =
			context.Source is WindowsStorageSource &&
			context.CoreModel is IFolder &&
			context.CoreModel is IWindowsStorable { IsStream: true, };

		if (!isArchiveFile && !isShellArchiveFolder)
		{
			return null;
		}

		if (context.GetCapability<IReadableContent>() is null)
		{
			return null;
		}

		var extensionSource = context.CoreModel
			is IWindowsStorable windowsStorable
				? windowsStorable.FileSystemPath ?? windowsStorable.ParsingName
				: context.CoreModel.Name;

		return _extensions.Any(extension => extensionSource.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
			? CreateSource(context)
			: null;
	}

	private IArchiveSource CreateSource(ItemContext context)
	{
		var rootSource = context.Source is ArchiveStorageSource archiveSource ? archiveSource.RootSource : context.Source;
		var locator = context.CoreModel is IArchiveEntry entry ? entry.Locator.Append(entry.EntryId, entry.EntryPath) : new ArchiveLocator(context.Reference);
		var models = context.ModelFactory ?? new StorableModelFactory();

		return new ArchiveSource(context.Reference, locator, rootSource, models, _readers, _credentials);
	}

	private static string NormalizeExtension(string extension)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(extension);

		var trimmedExtension = extension.Trim();

		return trimmedExtension[0] is '.'
			? trimmedExtension
			: $".{trimmedExtension}";
	}

	private sealed record ArchiveSource(StorableReference Archive, ArchiveLocator Locator, IStorageSource RootSource, IStorableModelFactory Models, ArchiveBackendSelector Readers,
		IArchiveCredentialResolver? Credentials) : IArchiveSource
	{
		public ValueTask<ArchiveModel> OpenAsync(CancellationToken cancellationToken = default)
		{
			return ArchiveModel.OpenAsync(RootSource, Locator, Models, Readers, Credentials, cancellationToken);
		}
	}
}

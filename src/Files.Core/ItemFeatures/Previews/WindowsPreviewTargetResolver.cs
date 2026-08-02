// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
using OwlCore.Storage;

namespace Files.Core.ItemFeatures.Previews;

public sealed class WindowsPreviewTargetResolver : IWindowsPreviewTargetResolver
{
	private readonly IFilesDataRoot dataRoot;

	public WindowsPreviewTargetResolver(IFilesDataRoot dataRoot)
	{
		ArgumentNullException.ThrowIfNull(dataRoot);
		this.dataRoot = dataRoot;
	}

	public async ValueTask<WindowsPreviewTarget> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(reference);

		var model = await dataRoot
			.ResolveAsync(reference, cancellationToken)
			.ConfigureAwait(false);

		try
		{
			if (model.Reference.SourceId != reference.SourceId
				|| !StringComparer.Ordinal.Equals(model.Reference.ItemId, reference.ItemId))
			{
				throw new InvalidDataException("The resolved preview target does not match the requested identity.");
			}

			if (model.CoreModel is not IWindowsStorable
				|| model.CoreModel is not IFile)
			{
				throw new NotSupportedException("The resolved preview target is not a Windows Shell-backed file.");
			}

			return new WindowsPreviewTarget(model, (IWindowsStorable)model.CoreModel);
		}
		catch (Exception resolutionError)
		{
			try
			{
				await model.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception cleanupError)
			{
				throw new AggregateException("Preview target resolution and model cleanup failed.", resolutionError, cleanupError);
			}

			throw;
		}
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
using OwlCore.Storage;

namespace Files.Core.Capabilities.Previews;

/// <summary>Resolves Files item references to owned Windows Shell preview targets.</summary>
public sealed class WindowsPreviewTargetResolver : IWindowsPreviewTargetResolver
{
	private readonly IStorageWorkspace _workspace;

	/// <summary>Initializes a Windows preview target resolver.</summary>
	/// <param name="workspace">The storage workspace.</param>
	public WindowsPreviewTargetResolver(IStorageWorkspace workspace)
	{
		ArgumentNullException.ThrowIfNull(workspace);

		_workspace = workspace;
	}

	/// <inheritdoc />
	public async ValueTask<WindowsPreviewTarget> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(reference);

		var model = await _workspace.ResolveAsync(reference, cancellationToken).ConfigureAwait(false);

		try
		{
			if (model.Reference.SourceId != reference.SourceId || !StringComparer.Ordinal.Equals(model.Reference.ItemId, reference.ItemId))
			{
				throw new InvalidDataException("The resolved preview target does not match the requested identity.");
			}

			var coreModel = model.GetCoreModel();
			if (coreModel is not IWindowsStorable windowsItem || coreModel is not IFile)
			{
				throw new NotSupportedException("The resolved preview target is not a Windows Shell-backed file.");
			}

			return new WindowsPreviewTarget(model, windowsItem);
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

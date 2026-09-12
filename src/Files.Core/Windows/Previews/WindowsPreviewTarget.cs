// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.IO;
using Files.Core.Capabilities;
using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Storage;

namespace Files.Core.Windows;

/// <summary>Owns a model and its Windows Shell item for preview operations.</summary>
public sealed class WindowsPreviewTarget : IDisposable, IAsyncDisposable
{
	private readonly IStorableModel _model;

	private readonly Lock _disposalLock = new();

	private Task? _disposeTask;

	/// <summary>Gets the Windows Shell item used for previewing.</summary>
	public IWindowsStorable Item { get; }

	/// <summary>Gets the underlying Files item model.</summary>
	public IStorableModel Model => _model;

	/// <summary>Gets the stable reference of the preview target.</summary>
	public StorableReference Reference => _model.Reference;

	internal ItemContext? Context { get; }

	/// <summary>Initializes a Windows preview target.</summary>
	/// <remarks>Use the overload that accepts an <see cref="ItemContext"/> when the target will be activated by a Shell preview session.</remarks>
	/// <param name="model">The item model to own.</param>
	/// <param name="item">The Windows Shell item.</param>
	public WindowsPreviewTarget(IStorableModel model, IWindowsStorable item)
		: this(model, item, null)
	{
	}

	/// <summary>Initializes a Windows preview target with the context required for activation-time policy checks.</summary>
	/// <param name="model">The item model to own.</param>
	/// <param name="item">The Windows Shell item.</param>
	/// <param name="context">The context used to revalidate the target before handler activation, or <see langword="null"/> for a target that cannot be activated yet.</param>
	public WindowsPreviewTarget(IStorableModel model, IWindowsStorable item, ItemContext? context)
	{
		ArgumentNullException.ThrowIfNull(model);
		ArgumentNullException.ThrowIfNull(item);

		if (!StringComparer.Ordinal.Equals(model.Reference.ItemId, item.Id))
		{
			throw new InvalidDataException("The target model and Windows item have different identities.");
		}

		if (context is not null && (!context.Reference.Equals(model.Reference) || !ReferenceEquals(context.CoreModel, item)))
		{
			throw new InvalidDataException("The target context does not describe the owned Windows item.");
		}

		_model = model;
		Item = item;
		Context = context;
	}

	/// <summary>Synchronously disposes the target model.</summary>
	public void Dispose()
	{
		DisposeAsync().AsTask().GetAwaiter().GetResult();
	}

	/// <summary>Asynchronously disposes the target model.</summary>
	/// <returns>A value task that represents the disposal operation.</returns>
	public ValueTask DisposeAsync()
	{
		lock (_disposalLock)
		{
			_disposeTask ??= _model.DisposeAsync().AsTask();

			return new ValueTask(_disposeTask);
		}
	}
}

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

			var source = _workspace.Sources.Single(source => source.SourceId == reference.SourceId);

			return new WindowsPreviewTarget(model, windowsItem, new ItemContext(source, coreModel, model.Reference));
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

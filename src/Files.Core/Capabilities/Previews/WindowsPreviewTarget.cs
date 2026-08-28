// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Windows;

namespace Files.Core.Capabilities.Previews;

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

	/// <summary>Initializes a Windows preview target.</summary>
	/// <param name="model">The item model to own.</param>
	/// <param name="item">The Windows Shell item.</param>
	public WindowsPreviewTarget(IStorableModel model, IWindowsStorable item)
	{
		ArgumentNullException.ThrowIfNull(model);
		ArgumentNullException.ThrowIfNull(item);

		if (!StringComparer.Ordinal.Equals(model.Reference.ItemId, item.Id))
		{
			throw new InvalidDataException("The target model and Windows item have different identities.");
		}

		_model = model;
		Item = item;
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

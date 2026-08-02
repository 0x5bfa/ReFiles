// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Windows;

namespace Files.Core.ItemFeatures.Previews;

public sealed class WindowsPreviewTarget : IDisposable, IAsyncDisposable
{
	private readonly IStorableModel _model;

	private readonly Lock _disposalLock = new();

	private Task? _disposeTask;

	public IWindowsStorable Item { get; }

	public IStorableModel Model => _model;

	public StorableReference Reference => _model.Reference;

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

	public void Dispose()
	{
		DisposeAsync().AsTask().GetAwaiter().GetResult();
	}

	public ValueTask DisposeAsync()
	{
		lock (_disposalLock)
		{
			_disposeTask ??= _model.DisposeAsync().AsTask();

			return new ValueTask(_disposeTask);
		}
	}
}

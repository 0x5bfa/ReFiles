// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Windows;

namespace Files.Core.ItemFeatures.Previews;

public sealed class WindowsPreviewTarget : IDisposable, IAsyncDisposable
{
	private readonly IStorableModel model;
	private readonly object disposalLock = new();
	private Task? disposeTask;

	public WindowsPreviewTarget(
		IStorableModel model,
		IWindowsStorable item)
	{
		ArgumentNullException.ThrowIfNull(model);
		ArgumentNullException.ThrowIfNull(item);

		if (!StringComparer.Ordinal.Equals(model.Reference.ItemId, item.Id))
		{
			throw new InvalidDataException(
				"The target model and Windows item have different identities.");
		}

		this.model = model;
		Item = item;
	}

	public IWindowsStorable Item { get; }

	public IStorableModel Model => model;

	public StorableReference Reference => model.Reference;

	public void Dispose()
	{
		DisposeAsync().AsTask().GetAwaiter().GetResult();
	}

	public ValueTask DisposeAsync()
	{
		lock (disposalLock)
		{
			disposeTask ??= model.DisposeAsync().AsTask();
			return new ValueTask(disposeTask);
		}
	}
}

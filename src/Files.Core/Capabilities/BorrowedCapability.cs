// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using System.IO;
using Files.Core.Browsing;
using Files.Core.Capabilities.Archives;
using Files.Core.Capabilities.Changes;
using Files.Core.Capabilities.Content;
using Files.Core.Capabilities.Previews;
using Files.Core.Capabilities.Properties;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Archives;
using Files.Core.ViewSettings;

namespace Files.Core.Capabilities;

internal static class BorrowedCapability
{
	public static void RegisterDefaults(CapabilityBuilder builder)
	{
		builder.SetBorrowedView<IReadableContent>(static readable => readable is IArchiveInput input ? new ArchiveContent(readable, input) : new ReadableContent(readable));
		builder.SetBorrowedView<IWritableContent>(static writable => new WritableContent(writable));
		builder.SetBorrowedView<IPropertySource>(static properties => properties is IBatchedPropertySource batch ? new BatchedProperties(properties, batch) : new Properties(properties));
		builder.SetBorrowedView<IPreviewSource>(static preview => new Preview(preview));
		builder.SetBorrowedView<IThumbnailSource>(static thumbnail => new Thumbnail(thumbnail));
		builder.SetBorrowedView<IArchiveSource>(static archive => new ArchiveSource(archive));
		builder.SetBorrowedView<IFolderChangeSource>(static changes =>
		{
			var view = new FolderChanges(changes);

			return new BorrowedCapabilityView<IFolderChangeSource>(view, view.Detach);
		});
		builder.SetBorrowedView<IFolderBrowseMetadata>(static metadata => new FolderMetadata(metadata));
	}

	private class ReadableContent(IReadableContent inner) : IReadableContent
	{
		public async Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
		{
			var stream = await inner.OpenReadAsync(cancellationToken).ConfigureAwait(false);

			return stream.CanWrite ? new ReadOnlyContentStream(stream) : stream;
		}
	}

	private sealed class ArchiveContent(IReadableContent inner, IArchiveInput input) : ReadableContent(inner), IArchiveInput
	{
		Task<Stream> IArchiveInput.OpenReadAsync(CancellationToken cancellationToken)
		{
			return input.OpenReadAsync(cancellationToken);
		}
	}

	private sealed class WritableContent(IWritableContent inner) : IWritableContent
	{
		public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default)
		{
			return inner.OpenWriteAsync(cancellationToken);
		}
	}

	private class Properties(IPropertySource inner) : IPropertySource
	{
		public ValueTask<IReadOnlyDictionary<string, object?>> GetPropertiesAsync(PropertyRequest request, CancellationToken cancellationToken = default)
		{
			return inner.GetPropertiesAsync(request, cancellationToken);
		}
	}

	private sealed class BatchedProperties(IPropertySource inner, IBatchedPropertySource batch) : Properties(inner), IBatchedPropertySource
	{
		public ItemContext Context => batch.Context;
		public IPropertyReader Reader => batch.Reader;
	}

	private sealed class Preview(IPreviewSource inner) : IPreviewSource
	{
		public ValueTask<PreviewResult?> GetPreviewAsync(PreviewRequest request, CancellationToken cancellationToken = default)
		{
			return inner.GetPreviewAsync(request, cancellationToken);
		}
	}

	private sealed class Thumbnail(IThumbnailSource inner) : IThumbnailSource
	{
		public ValueTask<ThumbnailResult?> GetThumbnailAsync(ThumbnailRequest request, CancellationToken cancellationToken = default)
		{
			return inner.GetThumbnailAsync(request, cancellationToken);
		}
	}

	private sealed class ArchiveSource(IArchiveSource inner) : IArchiveSource
	{
		public StorableReference Archive => inner.Archive;
		public ArchiveLocator Locator => inner.Locator;

		public ValueTask<IArchiveModel> OpenAsync(CancellationToken cancellationToken = default)
		{
			return inner.OpenAsync(cancellationToken);
		}
	}

	private sealed class FolderChanges : IFolderChangeSource
	{
		private readonly IFolderChangeSource _inner;
		private int _isDetached;

		public FolderChanges(IFolderChangeSource inner)
		{
			_inner = inner;
			inner.Changed += OnChanged;
			try
			{
				inner.Faulted += OnFaulted;
			}
			catch
			{
				inner.Changed -= OnChanged;

				throw;
			}
		}

		public event EventHandler<FolderChangeEventArgs>? Changed;
		public event EventHandler<FolderChangeErrorEventArgs>? Faulted;

		public ValueTask StartAsync(CancellationToken cancellationToken = default)
		{
			ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDetached) is not 0, this);

			return _inner.StartAsync(cancellationToken);
		}

		public void Detach()
		{
			if (Interlocked.Exchange(ref _isDetached, 1) is not 0)
			{
				return;
			}

			Changed = null;
			Faulted = null;
			try
			{
				_inner.Changed -= OnChanged;
			}
			finally
			{
				_inner.Faulted -= OnFaulted;
			}
		}

		private void OnChanged(object? sender, FolderChangeEventArgs args)
		{
			Publish(Changed, args);
		}

		private void OnFaulted(object? sender, FolderChangeErrorEventArgs args)
		{
			Publish(Faulted, args);
		}

		private void Publish<TArgs>(EventHandler<TArgs>? handlers, TArgs args)
		{
			if (handlers is null || Volatile.Read(ref _isDetached) is not 0)
			{
				return;
			}

			foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<TArgs>>())
			{
				try
				{
					handler(this, args);
				}
				catch (Exception error)
				{
					Trace.TraceError(error.ToString());
				}
			}
		}
	}

	private sealed class FolderMetadata(IFolderBrowseMetadata inner) : IFolderBrowseMetadata
	{
		public ValueTask<BrowseColumnSet?> GetColumnsAsync(CancellationToken cancellationToken = default)
		{
			return inner.GetColumnsAsync(cancellationToken);
		}

		public ValueTask<IReadOnlyList<IStorableModel>?> SortItemsAsync(IReadOnlyList<IStorableModel> items, BrowseViewSettings settings, CancellationToken cancellationToken)
		{
			return inner.SortItemsAsync(items, settings, cancellationToken);
		}
	}
}

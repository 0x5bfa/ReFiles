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
	public static TCapability Create<TCapability>(TCapability capability) where TCapability : class, ICapability
	{
		// Project after composition so wrappers cannot restore extra public roles or disposal authority.
		object borrowed = capability switch
		{
			IReadableContent readable when typeof(TCapability) == typeof(IReadableContent) => readable is IArchiveInput input ? new ArchiveContent(readable, input) : new ReadableContent(readable),
			IWritableContent writable when typeof(TCapability) == typeof(IWritableContent) => new WritableContent(writable),
			IPropertySource properties when typeof(TCapability) == typeof(IPropertySource) => properties is IBatchedPropertySource batch ? new BatchedProperties(properties, batch) : new Properties(properties),
			IPreviewSource preview when typeof(TCapability) == typeof(IPreviewSource) => new Preview(preview),
			IThumbnailSource thumbnail when typeof(TCapability) == typeof(IThumbnailSource) => new Thumbnail(thumbnail),
			IArchiveSource archive when typeof(TCapability) == typeof(IArchiveSource) => new ArchiveSource(archive),
			IFolderChangeSource changes when typeof(TCapability) == typeof(IFolderChangeSource) => new FolderChanges(changes),
			IFolderBrowseMetadata metadata when typeof(TCapability) == typeof(IFolderBrowseMetadata) => new FolderMetadata(metadata),
			_ => capability,
		};

		return (TCapability)borrowed;
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

		public ValueTask<ArchiveModel> OpenAsync(CancellationToken cancellationToken = default)
		{
			return inner.OpenAsync(cancellationToken);
		}
	}

	private sealed class FolderChanges : IFolderChangeSource
	{
		private readonly IFolderChangeSource _inner;

		public event EventHandler<FolderChangeEventArgs>? Changed;
		public event EventHandler<FolderChangeErrorEventArgs>? Faulted;

		public FolderChanges(IFolderChangeSource inner)
		{
			_inner = inner;
			inner.Changed += (_, args) => Publish(Changed, args);
			inner.Faulted += (_, args) => Publish(Faulted, args);
		}

		public ValueTask StartAsync(CancellationToken cancellationToken = default)
		{
			return _inner.StartAsync(cancellationToken);
		}

		private void Publish<TArgs>(EventHandler<TArgs>? handlers, TArgs args)
		{
			if (handlers is null)
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

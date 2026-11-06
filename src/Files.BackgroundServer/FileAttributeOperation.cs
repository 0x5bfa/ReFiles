// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using Files.Core.Storage;
using Files.Core.Storage.Windows;

namespace Files.BackgroundServer;

internal sealed class FileAttributeOperation : IServerOperation
{
	private readonly StorageOperationExecutor _executor;
	private readonly StorageReferenceData[] _references;
	private readonly FileAttributeChangesData _changes;
	private readonly StorageOperationData? _rename;

	public Guid Id { get; }
	public string Kind => "Attributes";
	public int ItemCount => _references.Length + (_rename.HasValue ? 1 : 0);
	public string? DestinationPath => null;
	public bool CanPause => false;

	internal FileAttributeOperation(Guid id, StorageReferenceData[] references, FileAttributeChangesData changes, StorageOperationExecutor executor, StorageOperationData? rename = null)
	{
		ArgumentNullException.ThrowIfNull(references);

		if (references.Length is 0 or > 10000)
		{
			throw new ArgumentException("The operation requires between 1 and 10000 storage references.", nameof(references));
		}

		foreach (var reference in references)
		{
			StorageOperationExecutor.ValidateReference(reference);
			ArgumentException.ThrowIfNullOrWhiteSpace(reference.ItemId);
		}

		foreach (var value in new[] { changes.ReadOnly, changes.Hidden, changes.Archive, changes.Indexed, changes.Compressed, changes.Encrypted })
		{
			if (value is < -1 or > 1)
			{
				throw new ArgumentException("An attribute change must be -1, 0, or 1.", nameof(changes));
			}
		}

		_executor = executor;
		Id = id;
		_references = (StorageReferenceData[])references.Clone();
		_changes = changes;
		_rename = rename;
		if (rename is { } request)
		{
			StorageOperationExecutor.Validate(request);
		}
	}

	public async Task ExecuteAsync(OperationExecutionContext context, CancellationToken cancellationToken)
	{
		for (var index = 0; index < _references.Length; index++)
		{
			var reference = StorageOperationExecutor.ToReference(_references[index]);
			var currentItemName = reference.LastKnownAddress?.Value ?? reference.ItemId;
			context.Report(new OperationProgress(index, ItemCount, CurrentItemName: currentItemName));
			await _executor.ApplyFileAttributesAsync(new[] { reference }, ToNullable(_changes.ReadOnly), ToNullable(_changes.Hidden), ToNullable(_changes.Archive), ToNullable(_changes.Indexed),
				ToNullable(_changes.Compressed), ToNullable(_changes.Encrypted), _changes.ApplyToContents, cancellationToken).ConfigureAwait(false);
			context.Report(new OperationProgress(index + 1, ItemCount, CurrentItemName: currentItemName));
		}

		if (_rename is { } rename)
		{
			var request = await _executor.CreateRequestAsync(rename, cancellationToken).ConfigureAwait(false);
			var progress = new StorageOperationProgressReporter(context, _references.Length, ItemCount, rename.DisplayName);
			var result = await _executor.ExecuteAsync(request, progress, context, cancellationToken).ConfigureAwait(false);
			if (!result.Succeeded)
			{
				throw result.Error!;
			}

			context.SetResult(result);
			context.Report(new OperationProgress(ItemCount, ItemCount, CurrentItemName: rename.DisplayName));
		}
	}

	private static bool? ToNullable(int value)
	{
		return value < 0 ? null : value == 1;
	}
}

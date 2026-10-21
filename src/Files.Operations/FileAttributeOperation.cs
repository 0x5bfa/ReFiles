// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using Files.Core.Windows;

namespace Files.Operations;

internal sealed class FileAttributeOperation : IServerOperation
{
	private readonly string[] _paths;
	private readonly FileAttributeChangesData _changes;
	private readonly StorageOperationData? _rename;

	public Guid Id { get; }
	public string Kind => "Attributes";
	public int ItemCount => _paths.Length + (_rename.HasValue ? 1 : 0);
	public string? DestinationPath => null;
	public bool CanPause => false;

	internal FileAttributeOperation(Guid id, string[] paths, FileAttributeChangesData changes, StorageOperationData? rename = null)
	{
		if (paths.Length is 0 or > 10000)
		{
			throw new ArgumentException("The operation requires between 1 and 10000 paths.", nameof(paths));
		}

		foreach (var path in paths)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(path);
		}

		foreach (var value in new[] { changes.ReadOnly, changes.Hidden, changes.Archive, changes.Indexed, changes.Compressed, changes.Encrypted })
		{
			if (value is < -1 or > 1)
			{
				throw new ArgumentException("An attribute change must be -1, 0, or 1.", nameof(changes));
			}
		}

		Id = id;
		_paths = (string[])paths.Clone();
		_changes = changes;
		_rename = rename;
		if (rename is { } request)
		{
			StorageOperationExecutor.Validate(request);
		}
	}

	public async Task ExecuteAsync(OperationExecutionContext context, CancellationToken cancellationToken)
	{
		for (var index = 0; index < _paths.Length; index++)
		{
			context.Report(new OperationProgress(index, ItemCount, CurrentItemName: _paths[index]));
			WindowsFileAttributeService.ApplyChanges(new[] { _paths[index] }, ToNullable(_changes.ReadOnly), ToNullable(_changes.Hidden), ToNullable(_changes.Archive), ToNullable(_changes.Indexed),
				ToNullable(_changes.Compressed), ToNullable(_changes.Encrypted), _changes.ApplyToContents, cancellationToken);
			context.Report(new OperationProgress(index + 1, ItemCount, CurrentItemName: _paths[index]));
		}

		if (_rename is { } rename)
		{
			var executor = Program.Storage.Value;
			var request = await executor.CreateRequestAsync(rename, cancellationToken).ConfigureAwait(false);
			var progress = new StorageOperationProgressReporter(context, _paths.Length, ItemCount, rename.DisplayName);
			var result = await executor.ExecuteAsync(request, progress, context, cancellationToken).ConfigureAwait(false);
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

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Sessions;

/// <summary>
/// Owns the identity and typed content of one pane.
/// </summary>
public sealed class PaneSession : IAsyncDisposable
{
	private readonly Lock _disposalLock = new();
	private Task? _disposeTask;

	/// <summary>
	/// Gets the stable pane identifier.
	/// </summary>
	public Guid Id { get; }

	/// <summary>
	/// Gets the content session owned by this pane.
	/// </summary>
	public IPaneContentSession Content { get; }

	/// <summary>
	/// Initializes a pane that owns the specified content session.
	/// </summary>
	/// <param name="content">The content session to own.</param>
	/// <param name="id">An optional stable pane identifier.</param>
	public PaneSession(IPaneContentSession content, Guid? id = null)
	{
		ArgumentNullException.ThrowIfNull(content);

		Id = id ?? Guid.NewGuid();
		if (Id == Guid.Empty)
		{
			throw new ArgumentException("A pane ID cannot be empty.", nameof(id));
		}

		Content = content;
	}

	/// <summary>
	/// Disposes the owned content session.
	/// </summary>
	/// <returns>A task that represents asynchronous disposal.</returns>
	public ValueTask DisposeAsync()
	{
		lock (_disposalLock)
		{
			_disposeTask ??= DisposeCoreAsync();

			return new ValueTask(_disposeTask);
		}
	}

	private async Task DisposeCoreAsync()
	{
		await Content.DisposeAsync().ConfigureAwait(false);
		GC.SuppressFinalize(this);
	}
}

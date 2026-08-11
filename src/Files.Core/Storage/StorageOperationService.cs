// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Routes requests to the first handler that supports them.
/// </summary>
public sealed class StorageOperationService : IStorageOperationService
{
	private readonly IReadOnlyList<IStorageOperationHandler> _handlers;

	/// <summary>Initializes a storage operation service.</summary>
	/// <param name="handlers">The handlers used to execute requests.</param>
	public StorageOperationService(IEnumerable<IStorageOperationHandler> handlers)
	{
		ArgumentNullException.ThrowIfNull(handlers);

		_handlers = handlers.ToArray();
		if (_handlers.Any(static handler => handler is null))
		{
			throw new ArgumentException("The handler collection cannot contain null entries.", nameof(handlers));
		}
	}

	/// <summary>Determines whether a registered handler supports a request.</summary>
	/// <param name="request">The operation request.</param>
	/// <returns><see langword="true"/> when a handler can execute the request.</returns>
	public bool CanHandle(StorageOperationRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		return _handlers.Any(handler => handler.CanHandle(request));
	}

	/// <summary>Executes a request through the first compatible handler.</summary>
	/// <param name="request">The operation request.</param>
	/// <param name="progress">The optional progress receiver.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The operation result.</returns>
	public async ValueTask<StorageOperationResult> ExecuteAsync(StorageOperationRequest request, IProgress<StorageOperationProgress>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();

		IStorageOperationHandler? handler = null;
		try
		{
			handler = _handlers.FirstOrDefault(candidate => candidate.CanHandle(request));
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			return Failed(exception);
		}

		if (handler is null)
		{
			return Failed(new NotSupportedException($"No storage operation handler can handle '{request.GetType().Name}'."));
		}

		try
		{
			StorageOperationResult? result = await handler.ExecuteAsync(request, progress, cancellationToken).ConfigureAwait(false);

			return result ?? Failed(new InvalidOperationException($"Storage operation handler '{handler.GetType().FullName}' returned null."));
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			return Failed(exception);
		}
	}

	private static StorageOperationResult Failed(Exception exception)
	{
		return new StorageOperationResult(false, null, exception);
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Routes requests to the first handler that supports them.
/// </summary>
public sealed class StorageOperationService : IStorageOperationService
{
	private readonly IReadOnlyList<IStorageOperationHandler> handlers;

	public StorageOperationService(IEnumerable<IStorageOperationHandler> handlers)
	{
		ArgumentNullException.ThrowIfNull(handlers);

		this.handlers = handlers.ToArray();
		if (this.handlers.Any(static handler => handler is null))
		{
			throw new ArgumentException("The handler collection cannot contain null entries.", nameof(handlers));
		}
	}

	public bool CanHandle(StorageOperationRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		return handlers.Any(handler => handler.CanHandle(request));
	}

	public async ValueTask<StorageOperationResult> ExecuteAsync(
		StorageOperationRequest request,
		IProgress<StorageOperationProgress>? progress = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();

		IStorageOperationHandler? handler = null;
		try
		{
			handler = handlers.FirstOrDefault(candidate => candidate.CanHandle(request));
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
			StorageOperationResult? result = await handler
				.ExecuteAsync(request, progress, cancellationToken)
				.ConfigureAwait(false);
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
		return new StorageOperationResult(Succeeded: false, ResultItem: null, Error: exception);
	}
}

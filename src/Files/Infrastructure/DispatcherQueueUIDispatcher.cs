// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Dispatching;

namespace Files.Infrastructure;

public sealed class DispatcherQueueUIDispatcher : IUIDispatcher
{
	private readonly DispatcherQueue _dispatcherQueue;

	public bool HasThreadAccess => _dispatcherQueue.HasThreadAccess;

	public DispatcherQueueUIDispatcher(DispatcherQueue dispatcherQueue)
	{
		ArgumentNullException.ThrowIfNull(dispatcherQueue);

		this._dispatcherQueue = dispatcherQueue;
	}

	public bool TryEnqueue(Action callback)
	{
		return TryEnqueue(DispatcherQueuePriority.Normal, callback);
	}

	public bool TryEnqueue(DispatcherQueuePriority priority, Action callback)
	{
		ArgumentNullException.ThrowIfNull(callback);

		return _dispatcherQueue.TryEnqueue(priority, () => callback());
	}
}

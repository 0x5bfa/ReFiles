// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Dispatching;

namespace Files.Infrastructure;

public sealed class DispatcherQueueUIDispatcher : IUIDispatcher
{
	private readonly DispatcherQueue dispatcherQueue;

	public bool HasThreadAccess => dispatcherQueue.HasThreadAccess;

	public DispatcherQueueUIDispatcher(DispatcherQueue dispatcherQueue)
	{
		ArgumentNullException.ThrowIfNull(dispatcherQueue);

		this.dispatcherQueue = dispatcherQueue;
	}

	public bool TryEnqueue(Action callback)
	{
		ArgumentNullException.ThrowIfNull(callback);

		return dispatcherQueue.TryEnqueue(() => callback());
	}
}

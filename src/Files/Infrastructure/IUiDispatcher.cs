// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Dispatching;

namespace Files.Infrastructure;

public interface IUIDispatcher
{
	bool HasThreadAccess { get; }

	bool TryEnqueue(Action callback);

	bool TryEnqueue(DispatcherQueuePriority priority, Action callback);
}

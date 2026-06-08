// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Infrastructure;

public interface IUIDispatcher
{
	bool HasThreadAccess { get; }

	bool TryEnqueue(Action callback);
}

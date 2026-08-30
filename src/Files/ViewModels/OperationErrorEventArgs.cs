// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.ViewModels;

internal sealed class OperationErrorEventArgs : EventArgs
{
	internal string Message { get; }

	internal OperationErrorEventArgs(string message)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(message);

		Message = message;
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Commands;

public enum CommandConcurrencyPolicy
{
	AllowParallel,
	CancelPrevious,
	RejectWhileRunning,
}

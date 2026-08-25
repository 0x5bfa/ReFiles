// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Sessions;

namespace Files.ViewModels;

internal sealed class SettingsPaneSession : IPaneContentSession
{
	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

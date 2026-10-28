// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Sessions;

/// <summary>Identifies independently owned application settings content.</summary>
public sealed class SettingsPaneSession : IPaneContentSession, IRestorablePaneContent
{
	/// <inheritdoc />
	public Func<IPaneContentSession> CaptureRestoreFactory()
	{
		return static () => new SettingsPaneSession();
	}

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}
}

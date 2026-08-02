// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Schedules Windows Shell COM work on message-pumped STA threads.
/// </summary>
[SupportedOSPlatform("windows")]
public interface IWindowsShellScheduler : IAsyncDisposable
{
	/// <summary>
	/// Runs apartment-affine work on the single ordered Shell STA lane.
	/// </summary>
	Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default);

	/// <summary>
	/// Runs independent Shell work on a small pool of Shell STA lanes. COM
	/// objects created by the delegate must not escape the delegate.
	/// </summary>
	Task<T> InvokeConcurrentAsync<T>(Func<T> action, CancellationToken cancellationToken = default);

	/// <summary>
	/// Runs long Shell operations on a separate ordered STA lane.
	/// </summary>
	Task<T> InvokeOperationAsync<T>(Func<T> action, CancellationToken cancellationToken = default);
}

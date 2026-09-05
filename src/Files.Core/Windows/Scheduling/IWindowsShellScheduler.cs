// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.Versioning;

namespace Files.Core.Windows;

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

	/// <summary>Runs blocking Shell search enumeration on its isolated STA lane.</summary>
	/// <typeparam name="T">The delegate result type.</typeparam>
	/// <param name="action">The synchronous delegate.</param>
	/// <param name="cancellationToken">The token used to cancel queuing.</param>
	/// <returns>A task containing the delegate result.</returns>
	/// <remarks>Implementations without a dedicated search lane fall back to the concurrent lane.</remarks>
	Task<T> InvokeSearchAsync<T>(Func<T> action, CancellationToken cancellationToken = default) => InvokeConcurrentAsync(action, cancellationToken);

	/// <summary>
	/// Runs long Shell operations on a separate ordered STA lane.
	/// </summary>
	Task<T> InvokeOperationAsync<T>(Func<T> action, CancellationToken cancellationToken = default);
}

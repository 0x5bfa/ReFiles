// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.Versioning;

namespace Files.Core.Windows;

/// <summary>
/// Schedules Windows Shell COM work on message-pumped STA threads.
/// </summary>
/// <remarks>
/// Delegates must be synchronous. Cancellation prevents pending work from starting;
/// active delegates must finish or observe cancellation themselves. A delegate must
/// not synchronously wait for queued work that depends on its own lane. COM calls
/// and message dispatch can reenter a delegate; STA execution does not prevent this.
/// Retained COM objects must be accessed and released on their owning fixed lane
/// before disposing the scheduler. Disposal rejects new work, faults pending work,
/// and waits for active delegates; never synchronously wait for it on a scheduler lane.
/// </remarks>
[SupportedOSPlatform("windows")]
public interface IWindowsShellScheduler : IAsyncDisposable
{
	/// <summary>
	/// Runs apartment-affine work on the single ordered Shell STA lane.
	/// </summary>
	/// <remarks>
	/// Queued work runs in FIFO order. Calls from the same lane execute immediately,
	/// ahead of queued work, provided the lane has not stopped accepting work.
	/// Work accepted before a concurrent stop may still execute.
	/// </remarks>
	/// <typeparam name="T">The delegate result type.</typeparam>
	/// <param name="action">The synchronous delegate.</param>
	/// <param name="cancellationToken">The token used to cancel queuing.</param>
	/// <returns>A task containing the delegate result.</returns>
	Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default);

	/// <summary>
	/// Runs independent Shell work on a small pool of Shell STA lanes. COM
	/// objects created by the delegate must not escape the delegate.
	/// </summary>
	/// <remarks>
	/// Separate invocations may use different STA threads. Calls from a worker in
	/// this pool execute immediately on that worker while the lane accepts work.
	/// </remarks>
	/// <typeparam name="T">The delegate result type.</typeparam>
	/// <param name="action">The synchronous delegate.</param>
	/// <param name="cancellationToken">The token used to cancel queuing.</param>
	/// <returns>A task containing the delegate result.</returns>
	Task<T> InvokeConcurrentAsync<T>(Func<T> action, CancellationToken cancellationToken = default);

	/// <summary>Runs blocking Shell search enumeration on its isolated STA lane.</summary>
	/// <typeparam name="T">The delegate result type.</typeparam>
	/// <param name="action">The synchronous delegate.</param>
	/// <param name="cancellationToken">The token used to cancel queuing.</param>
	/// <returns>A task containing the delegate result.</returns>
	/// <remarks>
	/// The Windows implementation uses a fixed lane with the same ordering and
	/// reentry rules as <see cref="InvokeAsync{T}"/>. Implementations without a
	/// dedicated search lane fall back to the concurrent lane.
	/// </remarks>
	Task<T> InvokeSearchAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
	{
		return InvokeConcurrentAsync(action, cancellationToken);
	}

	/// <summary>
	/// Runs long Shell operations on a separate ordered STA lane.
	/// </summary>
	/// <remarks>Uses the same ordering and reentry rules as <see cref="InvokeAsync{T}"/>.</remarks>
	/// <typeparam name="T">The delegate result type.</typeparam>
	/// <param name="action">The synchronous delegate.</param>
	/// <param name="cancellationToken">The token used to cancel queuing.</param>
	/// <returns>A task containing the delegate result.</returns>
	Task<T> InvokeOperationAsync<T>(Func<T> action, CancellationToken cancellationToken = default);
}

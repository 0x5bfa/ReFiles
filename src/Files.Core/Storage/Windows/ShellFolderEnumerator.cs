// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Windows.Win32.UI.Shell;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Keeps a Shell enumerator private and routes all access through its creating STA lane.
/// </summary>
internal sealed class ShellFolderEnumerator : IAsyncDisposable
{
	private readonly IWindowsShellScheduler scheduler;
	private readonly IWindowsItemIdReader itemIdReader;
	private IEnumShellItems? enumerator;
	private bool isCompleted;
	private int isDisposed;

	public ShellFolderEnumerator(
		IWindowsShellScheduler scheduler,
		IEnumShellItems enumerator,
		IWindowsItemIdReader itemIdReader)
	{
		ArgumentNullException.ThrowIfNull(scheduler);
		ArgumentNullException.ThrowIfNull(enumerator);
		ArgumentNullException.ThrowIfNull(itemIdReader);

		this.scheduler = scheduler;
		this.enumerator = enumerator;
		this.itemIdReader = itemIdReader;
	}

	public unsafe Task<IReadOnlyList<WindowsStorableDescriptor>> ReadNextAsync(
		int maximumCount,
		CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);
		ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) != 0, this);

		return scheduler.InvokeAsync<IReadOnlyList<WindowsStorableDescriptor>>(
			() =>
			{
				if (isCompleted)
				{
					return Array.Empty<WindowsStorableDescriptor>();
				}

				var nativeEnumerator = enumerator
					?? throw new ObjectDisposedException(nameof(ShellFolderEnumerator));
				var descriptors = new List<WindowsStorableDescriptor>(maximumCount);
				var children = new IShellItem[1];
				uint fetched = 0;

				while (descriptors.Count < maximumCount)
				{
					cancellationToken.ThrowIfCancellationRequested();
					var result = nativeEnumerator.Next(1, children, &fetched);

					if (result == global::Windows.Win32.Foundation.HRESULT.S_FALSE)
					{
						isCompleted = true;
						break;
					}

					result.ThrowOnFailure();
					descriptors.Add(ShellItemHelpers.CreateDescriptor(children[0], itemIdReader));
				}

				return descriptors;
			},
			cancellationToken);
	}

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref isDisposed, 1) != 0)
		{
			return ValueTask.CompletedTask;
		}

		return new ValueTask(DisposeCoreAsync());
	}

	private async Task DisposeCoreAsync()
	{
		await scheduler.InvokeAsync(
			() =>
			{
				enumerator = null;
				return true;
			}).ConfigureAwait(false);

		GC.SuppressFinalize(this);
	}
}

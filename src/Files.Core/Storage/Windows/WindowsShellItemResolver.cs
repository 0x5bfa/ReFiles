// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Windows.Win32;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Materializes Shell items on the scheduler and never returns a COM object to callers.
/// </summary>
internal sealed unsafe class WindowsShellItemResolver
{
	private readonly IWindowsShellScheduler _scheduler;

	public WindowsShellItemResolver(IWindowsShellScheduler scheduler)
	{
		ArgumentNullException.ThrowIfNull(scheduler);

		_scheduler = scheduler;
	}

	public Task<T> InvokeAsync<T>(WindowsItemLocator locator, Func<IShellItem, T> action, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(locator);
		ArgumentNullException.ThrowIfNull(action);

		return _scheduler.InvokeAsync(() => InvokeCore(locator, action), cancellationToken);
	}

	// Must be called on the ordered Shell STA because it creates and compares COM objects.
	internal static bool AreSamePidlOnCurrentSta(ReadOnlyMemory<byte> firstPidl, ReadOnlyMemory<byte> secondPidl)
	{
		if (firstPidl.IsEmpty || secondPidl.IsEmpty)
		{
			return false;
		}

		if (firstPidl.Span.SequenceEqual(secondPidl.Span))
		{
			return true;
		}

		var first = TryCreateFromPidl(firstPidl);
		var second = TryCreateFromPidl(secondPidl);

		return first is not null
			&& second is not null
			&& AreSame(first, second);
	}

	// Must be called on the ordered Shell STA because it creates and compares COM objects.
	internal static bool IsInFolderOnCurrentSta(ReadOnlyMemory<byte> itemPidl, ReadOnlyMemory<byte> folderPidl, bool recursive)
	{
		if (IsParentPidl(folderPidl, itemPidl, recursive))
		{
			return true;
		}

		var item = TryCreateFromPidl(itemPidl);
		var folder = TryCreateFromPidl(folderPidl);
		if (item is null || folder is null)
		{
			return false;
		}

		if (AreSame(item, folder))
		{
			return true;
		}

		var current = item;
		while (current.GetParent(out var parent).Succeeded && parent is not null)
		{
			if (AreSame(parent, folder))
			{
				return true;
			}

			if (!recursive)
			{
				return false;
			}

			current = parent;
		}

		return false;
	}

	public Task<T> InvokeConcurrentAsync<T>(WindowsItemLocator locator, Func<IShellItem, T> action, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(locator);
		ArgumentNullException.ThrowIfNull(action);

		return _scheduler.InvokeConcurrentAsync(() => InvokeCore(locator, action), cancellationToken);
	}

	public Task<T> InvokeOperationAsync<T>(WindowsItemLocator locator, Func<IShellItem, T> action, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(locator);
		ArgumentNullException.ThrowIfNull(action);

		return _scheduler.InvokeOperationAsync(() => InvokeCore(locator, action), cancellationToken);
	}

	public Task<T> InvokeOperationAsync<T>(string parsingName, Func<IShellItem, T> action, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);
		ArgumentNullException.ThrowIfNull(action);

		return _scheduler.InvokeOperationAsync(() => { var result = PInvoke.SHCreateItemFromParsingName(parsingName, null, out IShellItem shellItem);  result.ThrowOnFailure(); return action(shellItem); }, cancellationToken);
	}

	public Task<T> InvokeOperationAsync<T>(string firstParsingName, string secondParsingName, Func<IShellItem, IShellItem, T> action, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(firstParsingName);
		ArgumentException.ThrowIfNullOrWhiteSpace(secondParsingName);
		ArgumentNullException.ThrowIfNull(action);

		return _scheduler.InvokeOperationAsync(
			() =>
			{
				var firstResult = PInvoke.SHCreateItemFromParsingName(firstParsingName, null, out IShellItem first);
				firstResult.ThrowOnFailure();

				var secondResult = PInvoke.SHCreateItemFromParsingName(secondParsingName, null, out IShellItem second);
				secondResult.ThrowOnFailure();

				return action(first, second);
			},
			cancellationToken);
	}

	public Task<T> InvokeAsync<T>(ReadOnlyMemory<byte> absolutePidl, Func<IShellItem, T> action, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(action);

		if (absolutePidl.IsEmpty)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return Task.FromResult(default(T)!);
		}

		return _scheduler.InvokeAsync(() => { var shellItem = TryCreateFromPidl(absolutePidl); return shellItem is null ? default! : action(shellItem); }, cancellationToken);
	}

	public Task<T> InvokeAsync<T>(string parsingName, Func<IShellItem, T> action, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);
		ArgumentNullException.ThrowIfNull(action);

		return _scheduler.InvokeAsync(() => { var result = PInvoke.SHCreateItemFromParsingName(parsingName, null, out IShellItem shellItem);  result.ThrowOnFailure(); return action(shellItem); }, cancellationToken);
	}

	public Task<T> InvokeConcurrentAsync<T>(string parsingName, Func<IShellItem, T> action, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);
		ArgumentNullException.ThrowIfNull(action);

		return _scheduler.InvokeConcurrentAsync(() => { var result = PInvoke.SHCreateItemFromParsingName(parsingName, null, out IShellItem shellItem);  if (result.Failed) { return default!; }  return action(shellItem); }, cancellationToken);
	}

	private static T InvokeCore<T>(WindowsItemLocator locator, Func<IShellItem, T> action)
	{
		var shellItem = TryCreateFromPidl(locator.AbsolutePidl)
			?? CreateFromParsingName(locator.ParsingName);

		return shellItem is null ? default! : action(shellItem);
	}

	internal static unsafe IShellItem? TryCreateFromPidl(ReadOnlyMemory<byte> absolutePidl)
	{
		if (absolutePidl.IsEmpty)
		{
			return null;
		}

		fixed (byte* pidlBytes = absolutePidl.Span)
		{
			var interfaceId = typeof(IShellItem).GUID;
			void* itemPointer = null;
			var result = PInvoke.SHCreateItemFromIDList((ITEMIDLIST*)pidlBytes, &interfaceId, out object itemObject);

			if (result.Failed || itemObject is not IShellItem shellItem)
			{
				return null;
			}

			return shellItem;
		}
	}

	private static IShellItem? CreateFromParsingName(string parsingName)
	{
		var result = PInvoke.SHCreateItemFromParsingName(parsingName, null, out IShellItem shellItem);

		return result.Succeeded ? shellItem : null;
	}

	private static bool AreSame(IShellItem first, IShellItem second)
	{
		return first.Compare(second, unchecked((uint)_SICHINTF.SICHINT_ALLFIELDS), out var order).Succeeded
			&& order is 0;
	}

	private static unsafe bool IsParentPidl(ReadOnlyMemory<byte> folderPidl, ReadOnlyMemory<byte> itemPidl, bool recursive)
	{
		if (folderPidl.IsEmpty || itemPidl.IsEmpty)
		{
			return false;
		}

		fixed (byte* folderBytes = folderPidl.Span)
		fixed (byte* itemBytes = itemPidl.Span)
		{
			return PInvoke.ILIsParent((ITEMIDLIST*)folderBytes, (ITEMIDLIST*)itemBytes, !recursive);
		}
	}
}

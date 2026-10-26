// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Windows;
using Microsoft.UI.Xaml;
using System.Drawing;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;
using Windows.Win32;
using Windows.Win32.System.Com;

namespace Files.Infrastructure;

internal sealed partial class WinUIShellDropTargetController : IDisposable
{
	private readonly Func<StorableReference, bool, CancellationToken, Task<WindowsShellDropTarget?>> _prepareDropTargetAsync;
	private readonly nint _ownerWindowHandle;

	private CancellationTokenSource? _transitionCancellation;
	private Task? _transitionTask;
	private WinUIShellDropTargetLocation? _target;
	private WinUIShellDropTargetLocation? _fallback;
	private WindowsShellDropSession? _session;
	private WindowsShellDropEffects _acceptedEffect;
	private long _eventGeneration;
	private long _transitionGeneration;
	private bool _isDisposed;

	internal WinUIShellDropTargetController(Func<StorableReference, bool, CancellationToken, Task<WindowsShellDropTarget?>> prepareDropTargetAsync, nint ownerWindowHandle)
	{
		ArgumentNullException.ThrowIfNull(prepareDropTargetAsync);

		_prepareDropTargetAsync = prepareDropTargetAsync;
		_ownerWindowHandle = ownerWindowHandle;
	}

	internal Task DragEnterAsync(DragEventArgs args, WinUIShellDropTargetLocation target, WinUIShellDropTargetLocation? fallback = null)
	{
		return UpdateAsync(args, target, fallback, false);
	}

	internal Task DragOverAsync(DragEventArgs args, WinUIShellDropTargetLocation target, WinUIShellDropTargetLocation? fallback = null)
	{
		return UpdateAsync(args, target, fallback, true);
	}

	internal async Task DropAsync(DragEventArgs args, WinUIShellDropTargetLocation target, WinUIShellDropTargetLocation? fallback = null)
	{
		ArgumentNullException.ThrowIfNull(args);

		ArgumentNullException.ThrowIfNull(target);

		args.Handled = true;
		args.AcceptedOperation = DataPackageOperation.None;
		if (_isDisposed || _ownerWindowHandle is 0 || args.AllowedOperations is DataPackageOperation.None || !PInvoke.GetCursorPos(out var screenPoint))
		{
			ResetTransferState();

			return;
		}

		var generation = ++_eventGeneration;
		var dataView = args.DataView;
		var modifiers = ToShellModifiers(args.Modifiers);
		var allowedEffects = ToShellDropEffects(args.AllowedOperations);
		var dragState = new WinUIShellDragState(modifiers, screenPoint, allowedEffects);
		var deferral = args.GetDeferral();
		try
		{
			if (!await EnsureSessionAsync(dataView, target, fallback, dragState) || generation != _eventGeneration || _session is null)
			{
				return;
			}

			var effect = _session.Drop(dragState.Modifiers, dragState.ScreenPoint, dragState.AllowedEffects);
			UIDiagnosticLog.Write("ShellDropTarget", $"Drop allowed={dragState.AllowedEffects} effect={effect}");
			if (generation == _eventGeneration)
			{
				args.AcceptedOperation = WinUIDataObjectBridge.ToDataPackageOperation(effect);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			UIDiagnosticLog.Write("ShellDropTarget", $"Drop failed: {exception.Message}");
		}
		finally
		{
			ResetTransferState();
			deferral.Complete();
		}
	}

	internal void DragLeave()
	{
		if (_isDisposed)
		{
			return;
		}

		_eventGeneration++;
		ResetTransferState();
	}

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}

		_isDisposed = true;
		_eventGeneration++;
		ResetTransferState();
	}

	private async Task UpdateAsync(DragEventArgs args, WinUIShellDropTargetLocation target, WinUIShellDropTargetLocation? fallback, bool forwardDragOver)
	{
		ArgumentNullException.ThrowIfNull(args);

		ArgumentNullException.ThrowIfNull(target);

		args.Handled = true;
		args.AcceptedOperation = DataPackageOperation.None;
		if (_isDisposed)
		{
			return;
		}

		if (_ownerWindowHandle is 0 || args.AllowedOperations is DataPackageOperation.None || !PInvoke.GetCursorPos(out var screenPoint))
		{
			ResetTransferState();

			return;
		}

		var generation = ++_eventGeneration;
		var dataView = args.DataView;
		var modifiers = ToShellModifiers(args.Modifiers);
		var allowedEffects = ToShellDropEffects(args.AllowedOperations);
		var dragState = new WinUIShellDragState(modifiers, screenPoint, allowedEffects);
		var deferral = args.GetDeferral();
		try
		{
			if (!await EnsureSessionAsync(dataView, target, fallback, dragState) || generation != _eventGeneration || _session is null)
			{
				return;
			}

			var effect = _acceptedEffect;
			if (forwardDragOver)
			{
				effect = _session.DragOver(dragState.Modifiers, dragState.ScreenPoint, dragState.AllowedEffects);
				_acceptedEffect = effect;
			}
			else
			{
				UIDiagnosticLog.Write("ShellDropTarget", $"DragEnter allowed={dragState.AllowedEffects} effect={effect}");
			}

			if (generation == _eventGeneration)
			{
				args.AcceptedOperation = WinUIDataObjectBridge.ToDataPackageOperation(effect);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			UIDiagnosticLog.Write("ShellDropTarget", $"Drag feedback failed: {exception.Message}");
			ResetTransferState();
		}
		finally
		{
			deferral.Complete();
		}
	}

	private async Task<bool> EnsureSessionAsync(DataPackageView dataView, WinUIShellDropTargetLocation target, WinUIShellDropTargetLocation? fallback, WinUIShellDragState dragState)
	{
		if (_isDisposed)
		{
			return false;
		}

		if (MatchesTransfer(target, fallback) && _session is not null)
		{
			return true;
		}

		Task transition;
		if (MatchesTransfer(target, fallback) && _transitionTask is { IsCompleted: false } pendingTransition)
		{
			transition = pendingTransition;
		}
		else
		{
			ResetTransferState();
			IDataObject dataObject;
			try
			{
				dataObject = WinUIDataObjectBridge.GetDataObject(dataView);
			}
			catch (Exception exception)
			{
				UIDiagnosticLog.Write("ShellDropTarget", $"Native data object unavailable: {exception.Message}");

				return false;
			}

			var cancellation = new CancellationTokenSource();
			var transitionGeneration = ++_transitionGeneration;
			_target = target;
			_fallback = fallback;
			_transitionCancellation = cancellation;
			var context = new WinUIShellDropTransition(dataObject, dragState, transitionGeneration, cancellation.Token);
			transition = PrepareSessionAsync(target, fallback, context);
			_transitionTask = transition;
		}

		try
		{
			await transition;
		}
		finally
		{
			if (ReferenceEquals(_transitionTask, transition) && transition.IsCompleted)
			{
				_transitionTask = null;
				_transitionCancellation?.Dispose();
				_transitionCancellation = null;
			}
		}

		return !_isDisposed && MatchesTransfer(target, fallback) && _session is not null;
	}

	private async Task PrepareSessionAsync(WinUIShellDropTargetLocation target, WinUIShellDropTargetLocation? fallback, WinUIShellDropTransition context)
	{
		var enteredSession = await TryCreateEnteredSessionAsync(target, context);
		if (enteredSession is null && fallback is not null && fallback != target)
		{
			enteredSession = await TryCreateEnteredSessionAsync(fallback, context);
		}

		if (enteredSession is null)
		{
			return;
		}

		if (!IsTransitionCurrent(context.Generation, context.CancellationToken))
		{
			enteredSession.Session.Dispose();

			return;
		}

		_session = enteredSession.Session;
		_acceptedEffect = enteredSession.AcceptedEffect;
	}

	private async Task<WinUIShellDropSession?> TryCreateEnteredSessionAsync(WinUIShellDropTargetLocation location, WinUIShellDropTransition context)
	{
		WindowsShellDropTarget? dropTarget;
		try
		{
			dropTarget = await _prepareDropTargetAsync(location.Reference, location.Background, context.CancellationToken);
		}
		catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			UIDiagnosticLog.Write("ShellDropTarget", $"Target preparation failed: {exception.Message}");

			return null;
		}

		if (dropTarget is null || !IsTransitionCurrent(context.Generation, context.CancellationToken))
		{
			return null;
		}

		WindowsShellDropSession? session = null;
		try
		{
			if (!dropTarget.TryCreateSession(context.DataObject, _ownerWindowHandle, out session) || session is null)
			{
				return null;
			}

			if (!session.TryDragEnter(context.DragState.Modifiers, context.DragState.ScreenPoint, context.DragState.AllowedEffects, out var acceptedEffect))
			{
				session.Dispose();

				return null;
			}

			return new(session, acceptedEffect);
		}
		catch (Exception exception)
		{
			session?.Dispose();
			UIDiagnosticLog.Write("ShellDropTarget", $"Target entry failed: {exception.Message}");

			return null;
		}
	}

	private bool IsTransitionCurrent(long generation, CancellationToken cancellationToken)
	{
		return !_isDisposed && !cancellationToken.IsCancellationRequested && generation == _transitionGeneration;
	}

	private bool MatchesTransfer(WinUIShellDropTargetLocation target, WinUIShellDropTargetLocation? fallback)
	{
		return _target == target && _fallback == fallback;
	}

	private void ResetTransferState()
	{
		_transitionGeneration++;
		var transitionCancellation = _transitionCancellation;
		var session = _session;
		_transitionCancellation = null;
		_transitionTask = null;
		_session = null;
		_acceptedEffect = WindowsShellDropEffects.None;
		_target = null;
		_fallback = null;
		transitionCancellation?.Cancel();
		transitionCancellation?.Dispose();
		session?.Dispose();
	}

	private static WindowsShellDropEffects ToShellDropEffects(DataPackageOperation operations)
	{
		var effects = WindowsShellDropEffects.None;
		if (operations.HasFlag(DataPackageOperation.Copy))
		{
			effects |= WindowsShellDropEffects.Copy;
		}

		if (operations.HasFlag(DataPackageOperation.Move))
		{
			effects |= WindowsShellDropEffects.Move;
		}

		if (operations.HasFlag(DataPackageOperation.Link))
		{
			effects |= WindowsShellDropEffects.Link;
		}

		return effects;
	}

	private static WindowsShellDragDropModifiers ToShellModifiers(DragDropModifiers modifiers)
	{
		var result = WindowsShellDragDropModifiers.None;
		if (modifiers.HasFlag(DragDropModifiers.LeftButton))
		{
			result |= WindowsShellDragDropModifiers.LeftButton;
		}

		if (modifiers.HasFlag(DragDropModifiers.RightButton))
		{
			result |= WindowsShellDragDropModifiers.RightButton;
		}

		if (modifiers.HasFlag(DragDropModifiers.Shift))
		{
			result |= WindowsShellDragDropModifiers.Shift;
		}

		if (modifiers.HasFlag(DragDropModifiers.Control))
		{
			result |= WindowsShellDragDropModifiers.Control;
		}

		if (modifiers.HasFlag(DragDropModifiers.MiddleButton))
		{
			result |= WindowsShellDragDropModifiers.MiddleButton;
		}

		if (modifiers.HasFlag(DragDropModifiers.Alt))
		{
			result |= WindowsShellDragDropModifiers.Alt;
		}

		return result;
	}

}

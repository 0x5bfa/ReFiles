// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Files.Infrastructure;
using Windows.ApplicationModel.DataTransfer;

namespace Files.Commands;

public sealed class WindowCommandManager : IDisposable
{
	private readonly RootViewModel _root;
	private readonly IUIDispatcher _dispatcher;
	private readonly Dictionary<CommandId, ICommandHandler> _handlers;
	private readonly Dictionary<CommandId, CommandBindingViewModel> _bindings = [];
	private readonly Dictionary<CommandId, CommandCall> _activeCalls = [];
	private readonly CancellationTokenSource _lifetime = new();
	private readonly Lock _syncRoot = new();
	private int _isDisposed;

	public WindowCommandManager(RootViewModel root, CommandRegistry registry, IUIDispatcher dispatcher)
	{
		ArgumentNullException.ThrowIfNull(root);

		ArgumentNullException.ThrowIfNull(registry);

		ArgumentNullException.ThrowIfNull(dispatcher);

		_root = root;
		_dispatcher = dispatcher;
		_handlers = new(registry.CreateHandlers(root));
		foreach (var descriptor in registry.Descriptors)
		{
			_bindings.Add(descriptor.Id, new CommandBindingViewModel(this, descriptor));
		}

		Clipboard.ContentChanged += Clipboard_ContentChanged;
	}

	public CommandBindingViewModel GetBinding(CommandId id)
	{
		EnsureActive();

		if (!_bindings.TryGetValue(id, out var binding))
		{
			throw new KeyNotFoundException($"The command ID '{id}' is not registered.");
		}

		return binding;
	}

	public void RefreshStates(CommandStateInvalidation reasons = CommandStateInvalidation.All)
	{
		if (Volatile.Read(ref _isDisposed) is not 0 || reasons is CommandStateInvalidation.None)
		{
			return;
		}

		if (!_dispatcher.HasThreadAccess)
		{
			if (!_dispatcher.TryEnqueue(() =>
			{
				if (Volatile.Read(ref _isDisposed) is 0)
				{
					RefreshStates(reasons);
				}
			}))
			{
				throw new InvalidOperationException("The Files UI dispatcher rejected command state updates.");
			}

			return;
		}

		var context = new CommandContext(_root);
		foreach (var pair in _handlers)
		{
			if ((pair.Value.StateDependencies & reasons) is not 0)
			{
				_bindings[pair.Key].UpdateState(pair.Value.GetState(context));
			}
		}
	}

	public async Task<CommandExecutionResult> ExecuteAsync(CommandId id, object? parameter = null, CancellationToken cancellationToken = default)
	{
		EnsureActive();

		if (!_handlers.TryGetValue(id, out var handler))
		{
			throw new KeyNotFoundException($"The command ID '{id}' is not registered.");
		}

		var context = new CommandContext(_root, parameter);
		var state = handler.GetState(context);
		if (!state.IsVisible || !state.IsEnabled)
		{
			return CommandExecutionResult.Unsupported();
		}

		CommandCall call;
		CommandCall? previousCall = null;
		lock (_syncRoot)
		{
			EnsureActive();

			if (handler.ConcurrencyPolicy is CommandConcurrencyPolicy.RejectWhileRunning && _activeCalls.ContainsKey(id))
			{
				return CommandExecutionResult.Unsupported();
			}

			if (handler.ConcurrencyPolicy is CommandConcurrencyPolicy.CancelPrevious && _activeCalls.TryGetValue(id, out previousCall))
			{
				previousCall.Cancel();
			}

			call = new CommandCall(cancellationToken, _lifetime.Token);
			_activeCalls[id] = call;
		}

		try
		{
			if (previousCall is not null)
			{
				await previousCall.Completion.ConfigureAwait(false);
				call.Token.ThrowIfCancellationRequested();
			}

			var result = await handler
				.ExecuteAsync(context, call.Token)
				.ConfigureAwait(false);
			if (result.Status is CommandExecutionStatus.Failed && result.Error is { } error)
			{
				ReportError(error);
			}

			return result;
		}
		catch (OperationCanceledException)
		{
			return CommandExecutionResult.Canceled();
		}
		catch (Exception exception)
		{
			ReportError(exception);

			return CommandExecutionResult.Failed(exception);
		}
		finally
		{
			lock (_syncRoot)
			{
				if (_activeCalls.TryGetValue(id, out var active) && ReferenceEquals(active, call))
				{
					_activeCalls.Remove(id);
				}
			}

			call.Dispose();
			if (Volatile.Read(ref _isDisposed) is 0)
			{
				RefreshStates(handler.StateDependencies);
			}
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		_lifetime.Cancel();
		lock (_syncRoot)
		{
			foreach (var call in _activeCalls.Values)
			{
				call.Cancel();
			}

			_activeCalls.Clear();
		}

		_bindings.Clear();
		_handlers.Clear();
		Clipboard.ContentChanged -= Clipboard_ContentChanged;
		_lifetime.Dispose();
	}

	internal bool CanExecute(CommandId id, object? parameter)
	{
		if (Volatile.Read(ref _isDisposed) is not 0 || !_handlers.TryGetValue(id, out var handler))
		{
			return false;
		}

		var state = handler.GetState(new CommandContext(_root, parameter));

		return state.IsVisible && state.IsEnabled;
	}

	private void Clipboard_ContentChanged(object? sender, object args) =>
		RefreshStates(CommandStateInvalidation.Clipboard);

	private void ReportError(Exception exception)
	{
		if (Volatile.Read(ref _isDisposed) is not 0)
		{
			return;
		}

		if (_dispatcher.HasThreadAccess)
		{
			_root.ReportOperationError(exception);

			return;
		}

		if (!_dispatcher.TryEnqueue(() => _root.ReportOperationError(exception)))
		{
			throw new InvalidOperationException("The Files UI dispatcher rejected a command error.", exception);
		}
	}

	private void EnsureActive() =>
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) is not 0, this);

	private sealed class CommandCall : IDisposable
	{
		private readonly CancellationTokenSource _cancellation;
		private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public CancellationToken Token { get; }

		public Task Completion => _completion.Task;

		public CommandCall(CancellationToken cancellationToken, CancellationToken lifetimeToken)
		{
			_cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeToken);
			Token = _cancellation.Token;
		}

		public void Cancel()
		{
			try
			{
				_cancellation.Cancel();
			}
			catch (ObjectDisposedException)
			{
			}
		}

		public void Dispose()
		{
			_cancellation.Dispose();
			_completion.TrySetResult(true);
		}
	}
}

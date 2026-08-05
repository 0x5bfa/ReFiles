// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Files.Localization;

namespace Files.Commands;

public sealed partial class CommandBindingViewModel : ObservableObject
{
	private readonly WindowCommandManager manager;
	private CommandState state = new(false, false);

	internal CommandBindingViewModel(WindowCommandManager manager, CommandDescriptor descriptor)
	{
		this.manager = manager;
		Descriptor = descriptor;
		Command = new BindingCommand(this);
	}

	public CommandId Id => Descriptor.Id;

	public CommandDescriptor Descriptor { get; }

	public string Label => Descriptor.LabelResourceKey.GetLocalized();

	public string IconKey => Descriptor.IconKey;

	public string? Glyph => Descriptor.Glyph;

	public string ToolTip => Label;

	public string AutomationName => Label;

	public ICommand Command { get; }

	public bool IsVisible => state.IsVisible;

	public bool IsEnabled => state.IsEnabled;

	public bool IsChecked => state.IsChecked;

	public string? DisabledReasonResourceKey =>
		state.DisabledReasonResourceKey;

	public Task<CommandExecutionResult> ExecuteAsync(object? parameter = null, CancellationToken cancellationToken = default) =>
		manager.ExecuteAsync(Id, parameter, cancellationToken);

	internal void UpdateState(CommandState newState)
	{
		ArgumentNullException.ThrowIfNull(newState);

		if (Equals(state, newState))
		{
			return;
		}

		var visibleChanged = IsVisible != newState.IsVisible;
		var enabledChanged = IsEnabled != newState.IsEnabled;
		var checkedChanged = IsChecked != newState.IsChecked;
		state = newState;

		if (visibleChanged)
		{
			OnPropertyChanged(nameof(IsVisible));
		}

		if (enabledChanged)
		{
			OnPropertyChanged(nameof(IsEnabled));
		}

		if (checkedChanged)
		{
			OnPropertyChanged(nameof(IsChecked));
		}

		OnPropertyChanged(nameof(DisabledReasonResourceKey));
		if (enabledChanged)
		{
			((BindingCommand)Command).RaiseCanExecuteChanged();
		}
	}

	private async Task ExecuteFromBindingAsync(object? parameter)
	{
		await ExecuteAsync(parameter).ConfigureAwait(false);
	}

	private sealed partial class BindingCommand(CommandBindingViewModel owner) : ICommand
	{
		public event EventHandler? CanExecuteChanged;

		public bool CanExecute(object? parameter) => owner.IsEnabled;

		public void Execute(object? parameter) =>
			_ = owner.ExecuteFromBindingAsync(parameter);

		public void RaiseCanExecuteChanged() =>
			CanExecuteChanged?.Invoke(this, EventArgs.Empty);
	}
}

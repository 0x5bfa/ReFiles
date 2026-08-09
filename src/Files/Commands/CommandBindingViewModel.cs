// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Files.Controls;
using Files.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.Commands;

public sealed partial class CommandBindingViewModel : ObservableObject
{
	private readonly WindowCommandManager _manager;
	private CommandState _state = new(false, false);

	public CommandId Id => Descriptor.Id;

	public CommandDescriptor Descriptor { get; }

	public string Label => Descriptor.LabelResourceKey.GetLocalized();

	public ThemedIconData? IconData { get; }

	public IconSource? IconSource { get; }

	public string? Glyph => Descriptor.Glyph;

	public string ToolTip => Label;

	public string AutomationName => Label;

	public ICommand Command { get; }

	public bool IsVisible => _state.IsVisible;

	public bool IsEnabled => _state.IsEnabled;

	public bool IsChecked => _state.IsChecked;

	public string? DisabledReasonResourceKey =>
		_state.DisabledReasonResourceKey;

	internal CommandBindingViewModel(WindowCommandManager manager, CommandDescriptor descriptor)
	{
		_manager = manager;
		Descriptor = descriptor;
		IconData = ResolveIconData(descriptor.IconResourceKey);
		IconSource = IconData is null ? null : new ThemedIconSource { Data = IconData, IconSize = 16 };
		Command = new BindingCommand(this);
	}

	public Task<CommandExecutionResult> ExecuteAsync(object? parameter = null, CancellationToken cancellationToken = default) =>
		_manager.ExecuteAsync(Id, parameter, cancellationToken);

	internal void UpdateState(CommandState newState)
	{
		ArgumentNullException.ThrowIfNull(newState);

		if (Equals(_state, newState))
		{
			((BindingCommand)Command).RaiseCanExecuteChanged();

			return;
		}

		var visibleChanged = IsVisible != newState.IsVisible;
		var enabledChanged = IsEnabled != newState.IsEnabled;
		var checkedChanged = IsChecked != newState.IsChecked;
		_state = newState;

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
		((BindingCommand)Command).RaiseCanExecuteChanged();
	}

	private async Task ExecuteFromBindingAsync(object? parameter)
	{
		await ExecuteAsync(parameter).ConfigureAwait(false);
	}

	private static ThemedIconData? ResolveIconData(string? resourceKey)
	{
		if (resourceKey is null || Application.Current?.Resources.TryGetValue(resourceKey, out var value) is not true)
		{
			return null;
		}

		return value as ThemedIconData;
	}

	private sealed partial class BindingCommand(CommandBindingViewModel owner) : ICommand
	{
		public event EventHandler? CanExecuteChanged;

		public bool CanExecute(object? parameter) => owner._manager.CanExecute(owner.Id, parameter);

		public void Execute(object? parameter) =>
			_ = owner.ExecuteFromBindingAsync(parameter);

		public void RaiseCanExecuteChanged() =>
			CanExecuteChanged?.Invoke(this, EventArgs.Empty);
	}
}

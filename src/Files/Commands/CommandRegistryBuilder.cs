// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Commands;

using Files.ViewModels;

public sealed class CommandRegistryBuilder
{
	private readonly Dictionary<CommandId, CommandRegistry.CommandRegistration>
		registrations = [];
	private bool isBuilt;

	public CommandRegistryBuilder Register(CommandDescriptor descriptor, Func<RootViewModel, ICommandHandler> factory)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		ArgumentNullException.ThrowIfNull(factory);
		EnsureNotBuilt();

		if (!registrations.TryAdd(descriptor.Id, new CommandRegistry.CommandRegistration(descriptor, factory)))
		{
			throw new InvalidOperationException($"The command ID '{descriptor.Id}' is already registered.");
		}

		return this;
	}

	public CommandRegistry Build()
	{
		EnsureNotBuilt();
		isBuilt = true;
		return new CommandRegistry(new Dictionary<CommandId, CommandRegistry.CommandRegistration>(registrations));
	}

	private void EnsureNotBuilt()
	{
		if (isBuilt)
		{
			throw new InvalidOperationException("The command registry has already been built.");
		}
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;

namespace Files.Commands;

public sealed class CommandRegistry
{
	private readonly IReadOnlyDictionary<CommandId, CommandRegistration> registrations;

	internal CommandRegistry(
		IReadOnlyDictionary<CommandId, CommandRegistration> registrations)
	{
		this.registrations = registrations;
		Descriptors = registrations.Values
			.Select(static registration => registration.Descriptor)
			.OrderBy(static descriptor => descriptor.GroupResourceKey)
			.ThenBy(static descriptor => descriptor.Order)
			.ToArray()
			.AsReadOnly();
	}

	public IReadOnlyList<CommandDescriptor> Descriptors { get; }

	internal IReadOnlyDictionary<CommandId, ICommandHandler> CreateHandlers(
		RootViewModel root)
	{
		ArgumentNullException.ThrowIfNull(root);
		return registrations.ToDictionary(
			static entry => entry.Key,
			entry => entry.Value.Factory(root));
	}

	internal sealed record CommandRegistration(
		CommandDescriptor Descriptor,
		Func<RootViewModel, ICommandHandler> Factory);
}

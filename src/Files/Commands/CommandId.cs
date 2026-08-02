// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Commands;

public readonly record struct CommandId
{
	public string Value { get; }

	public CommandId(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);

		Value = value;
	}

	public override string ToString() => Value;
}

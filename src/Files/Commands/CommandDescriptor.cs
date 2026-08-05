// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Commands;

public sealed record CommandDescriptor(CommandId Id, string LabelResourceKey, string IconKey, string GroupResourceKey, int Order, string? Glyph = null);

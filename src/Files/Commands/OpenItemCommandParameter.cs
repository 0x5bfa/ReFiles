// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;
using Files.Core.Windows;

namespace Files.Commands;

internal sealed record OpenItemCommandParameter(BrowseItemViewModel Item, WindowsShellInvocationPoint? InvocationPoint);

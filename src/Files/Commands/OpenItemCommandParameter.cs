// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Windows;
using Files.ViewModels;

namespace Files.Commands;

internal sealed record OpenItemCommandParameter(BrowseItemViewModel Item, WindowsShellInvocationPoint? InvocationPoint);

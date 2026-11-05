// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Archives;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Archives;

namespace Files.Core.Sessions;

public sealed partial class BrowsePaneSession
{
	IBrowseNavigationModel? IBrowsePaneSession.Navigation => Navigation;

	IBrowseNavigationHistory IBrowsePaneSession.History => History;
}

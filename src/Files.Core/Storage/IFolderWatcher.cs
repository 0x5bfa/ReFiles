// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Specialized;

namespace Files.Core.Storage;

/// <summary>
/// Watches for changes in a mutable folder.
/// </summary>
public interface IFolderWatcher : INotifyCollectionChanged, IDisposable, IAsyncDisposable
{
	/// <summary>
	/// Gets the folder being watched.
	/// </summary>
	IMutableFolder Folder { get; }
}

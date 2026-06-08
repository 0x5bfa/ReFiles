// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;
using Files.Core.Storage;
using OwlCore.Storage;

namespace Files.Core.Models;

/// <summary>
/// Files-specific application model for an OwlCore storage item.
/// </summary>
public interface IStorableModel : IHasItemFeatures, IDisposable, IAsyncDisposable
{
	IStorable CoreModel { get; }

	StorableReference Reference { get; }

	string Name { get; }

}

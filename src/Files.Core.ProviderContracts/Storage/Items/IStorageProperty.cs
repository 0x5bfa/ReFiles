// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;

namespace Files.Core.Storage;

/// <summary>
/// Represents a disposable property value retrieved from a storage item.
/// </summary>
/// <typeparam name="T">The type of the property value.</typeparam>
public interface IStorageProperty<T> : IDisposable
{
	/// <summary>
	/// Gets the current property value.
	/// </summary>
	T Value { get; }

	/// <summary>
	/// Occurs when the property value changes.
	/// </summary>
	event EventHandler<T>? ValueUpdated;
}

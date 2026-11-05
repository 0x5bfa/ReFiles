// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Models;

/// <summary>Provides explicit provider-side access to a model's borrowed raw item.</summary>
public interface IStorageModelAccess
{
	/// <summary>Gets the raw item while its model is alive.</summary>
	/// <returns>The borrowed raw item.</returns>
	IStorable GetCoreModel();
}

/// <summary>Resolves provider-facing raw item access for runtime-created models.</summary>
public static class StorableModelAccess
{
	/// <summary>Gets a model's borrowed raw storage item.</summary>
	/// <param name="model">The model to inspect.</param>
	/// <returns>The borrowed raw item.</returns>
	public static IStorable GetCoreModel(this IStorableModel model)
	{
		ArgumentNullException.ThrowIfNull(model);

		return model is IStorageModelAccess internalModel
			? internalModel.GetCoreModel()
			: throw new InvalidOperationException($"Model type '{model.GetType().FullName}' does not expose an internal storage item.");
	}
}

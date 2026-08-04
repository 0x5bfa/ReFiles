// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using OwlCore.Storage;

namespace Files.Core.Models;

internal interface IStorableModelInternal
{
	IStorable GetCoreModel();
}

internal static class StorableModelAccess
{
	public static IStorable GetCoreModel(this IStorableModel model)
	{
		ArgumentNullException.ThrowIfNull(model);

		return model is IStorableModelInternal internalModel
			? internalModel.GetCoreModel()
			: throw new InvalidOperationException($"Model type '{model.GetType().FullName}' does not expose an internal storage item.");
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using OwlCore.Storage;

namespace Files.Core.Models;

public interface IStorableModelFactory
{
	IStorableModel Create(IStorageSource source, IStorable coreModel);
}

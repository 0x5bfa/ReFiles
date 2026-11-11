// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Data;
using Files.Core.Storage;

namespace Files.Settings;

internal static class AppSettingsStorageFactory
{
	internal static ISettingsStorage Create(IStorageReader workspace, IStorageOperationService operations, string localFolderPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(localFolderPath);

		ArgumentNullException.ThrowIfNull(workspace);
		ArgumentNullException.ThrowIfNull(operations);

		return new LocalSettingsStorage(workspace, operations, localFolderPath);
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Settings;

internal interface ISettingsStorage
{
	Task<string?> ReadTextAsync(string fileName, CancellationToken cancellationToken = default);
	Task WriteTextAsync(string fileName, string text, CancellationToken cancellationToken = default);
}

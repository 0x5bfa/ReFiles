// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json.Serialization;

namespace Files.Settings;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ViewSettingsDocument))]
internal partial class ViewSettingsJsonContext : JsonSerializerContext
{
}

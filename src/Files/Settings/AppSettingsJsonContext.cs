// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Files.Settings;

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettingsData))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}

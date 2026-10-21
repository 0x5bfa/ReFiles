// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json.Serialization;

namespace Files.UITests;

[JsonSerializable(typeof(BrowsePerformanceResult))]
[JsonSerializable(typeof(IReadOnlyList<BrowsePerformanceResult>), TypeInfoPropertyName = "Results")]
internal partial class BrowsePerformanceJsonContext : JsonSerializerContext;

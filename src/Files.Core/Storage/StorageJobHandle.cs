// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>Identifies a job within one server lifetime.</summary>
/// <param name="Id">The caller-selected operation identity.</param>
/// <param name="ServerInstanceId">The execution server lifetime identity.</param>
public sealed record StorageJobHandle(Guid Id, Guid ServerInstanceId);

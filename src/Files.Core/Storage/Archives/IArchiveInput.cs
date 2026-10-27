// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Storage.Archives;

internal interface IArchiveInput
{
	Task<Stream> OpenReadAsync(CancellationToken cancellationToken);
}

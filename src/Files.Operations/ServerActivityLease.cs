// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Threading;

namespace Files.Operations;

internal sealed class ServerActivityLease(ServerLifetime owner, bool isClient) : IDisposable
{
	private ServerLifetime? _owner = owner;

	public void Dispose()
	{
		Interlocked.Exchange(ref _owner, null)?.Release(isClient);
	}
}

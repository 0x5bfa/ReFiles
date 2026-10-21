// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Diagnostics;
using System.Threading;

namespace Files.Operations;

public sealed class AppService : IDisposable
{
	private static int _clientCount;
	private readonly Process _process;
	private int _disposed;

	public AppService(int processId)
	{
		_process = Process.GetProcessById(processId);
		Interlocked.Increment(ref _clientCount);
		try
		{
			_process.Exited += Process_Exited;
			_process.EnableRaisingEvents = true;
		}
		catch
		{
			_process.Exited -= Process_Exited;
			_process.Dispose();
			if (Interlocked.Decrement(ref _clientCount) == 0)
			{
				Program.ExitSignal.TrySetResult(true);
			}

			throw;
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		_process.Exited -= Process_Exited;
		_process.Dispose();
		if (Interlocked.Decrement(ref _clientCount) == 0)
		{
			Program.ExitSignal.TrySetResult(true);
		}

		GC.SuppressFinalize(this);
	}

	~AppService() => Dispose();

	private void Process_Exited(object? sender, EventArgs e) => Dispose();
}

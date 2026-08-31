// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Diagnostics;

namespace Files.Operations;

public sealed class AppInstanceMonitor
{
	private static readonly object _syncRoot = new();
	private static readonly HashSet<Process> _processes = [];
	private static bool _isStopping;

	/// <summary>
	/// Keeps the operation server alive until the specified process exits.
	/// </summary>
	/// <param name="processId">The identifier of the process to monitor.</param>
	public static void StartMonitor(int processId)
	{
		var process = Process.GetProcessById(processId);
		process.Exited += Process_Exited;

		lock (_syncRoot)
		{
			if (_isStopping)
			{
				process.Exited -= Process_Exited;
				process.Dispose();

				throw new InvalidOperationException("The operation server is already stopping.");
			}

			_ = _processes.Add(process);
		}

		try
		{
			process.EnableRaisingEvents = true;
		}
		catch
		{
			ReleaseProcess(process);

			throw;
		}
	}

	private static void Process_Exited(object? sender, EventArgs e)
	{
		if (sender is Process process)
		{
			ReleaseProcess(process);
		}
	}

	private static void ReleaseProcess(Process process)
	{
		var shouldStop = false;
		lock (_syncRoot)
		{
			if (!_processes.Remove(process))
			{
				return;
			}

			if (_processes.Count is 0)
			{
				_isStopping = true;
				shouldStop = true;
			}
		}

		process.Exited -= Process_Exited;
		process.Dispose();

		if (shouldStop)
		{
			Program.ExitSignal.TrySetResult(true);
		}
	}
}

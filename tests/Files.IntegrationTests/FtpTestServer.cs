// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Files.IntegrationTests;

internal sealed class FtpTestServer : IAsyncDisposable
{
	private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(15);
	private readonly Process _process;
	private readonly Task<string> _standardOutput;
	private readonly Task<string> _standardError;
	private readonly string _rootPath;
	private int _isDisposed;

	public string Host { get; } = "127.0.0.1";

	public int Port { get; }

	public string UserName { get; } = "refiles-test";

	public string Password { get; } = "refiles-test-password";

	private FtpTestServer(Process process, Task<string> standardOutput, Task<string> standardError, string rootPath, int port)
	{
		_process = process;
		_standardOutput = standardOutput;
		_standardError = standardError;
		_rootPath = rootPath;
		Port = port;
	}

	public static async Task<FtpTestServer> StartAsync()
	{
		var rootPath = Path.Combine(Path.GetTempPath(), $"ReFiles.FtpIntegrationTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(rootPath);
		var port = GetFreePort();
		var startInfo = CreateStartInfo(rootPath, port);

		try
		{
			var process = Process.Start(startInfo)
				?? throw new InvalidOperationException("Python could not be started.");
			var server = new FtpTestServer(process, process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync(), rootPath, port);
			try
			{
				await server.WaitForReadyAsync().ConfigureAwait(false);

				return server;
			}
			catch
			{
				await server.DisposeAsync().ConfigureAwait(false);

				throw;
			}
		}
		catch (System.ComponentModel.Win32Exception exception)
		{
			TryDeleteDirectory(rootPath);

			throw new InvalidOperationException("Python is required for FTP integration tests. Install Python and pyftpdlib, or set REFILES_PYTHON_PATH to a Python executable.", exception);
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		try
		{
			if (!_process.HasExited)
			{
				_process.Kill(entireProcessTree: true);
			}
		}
		catch (InvalidOperationException)
		{
			// The process exited between HasExited and Kill.
		}

		try
		{
			await _process.WaitForExitAsync().ConfigureAwait(false);
		}
		catch (InvalidOperationException)
		{
			// The process handle was already closed by the operating system.
		}

		await Task.WhenAll(_standardOutput, _standardError).ConfigureAwait(false);
		_process.Dispose();
		TryDeleteDirectory(_rootPath);
		GC.SuppressFinalize(this);
	}

	private static ProcessStartInfo CreateStartInfo(string rootPath, int port)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = Environment.GetEnvironmentVariable("REFILES_PYTHON_PATH") ?? "python",
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			WorkingDirectory = rootPath,
		};
		startInfo.ArgumentList.Add("-m");
		startInfo.ArgumentList.Add("pyftpdlib");
		startInfo.ArgumentList.Add("--interface");
		startInfo.ArgumentList.Add("127.0.0.1");
		startInfo.ArgumentList.Add("--port");
		startInfo.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));
		startInfo.ArgumentList.Add("--username");
		startInfo.ArgumentList.Add("refiles-test");
		startInfo.ArgumentList.Add("--password");
		startInfo.ArgumentList.Add("refiles-test-password");
		startInfo.ArgumentList.Add("--write");
		startInfo.ArgumentList.Add("--directory");
		startInfo.ArgumentList.Add(rootPath);
		startInfo.ArgumentList.Add("--encoding");
		startInfo.ArgumentList.Add("utf8");

		return startInfo;
	}

	private async Task WaitForReadyAsync()
	{
		using var timeout = new CancellationTokenSource(StartupTimeout);
		while (true)
		{
			if (_process.HasExited)
			{
				var error = await _standardError.ConfigureAwait(false);

				throw new InvalidOperationException($"The pyftpdlib server exited before it accepted connections: {error.Trim()}");
			}

			using var client = new TcpClient();
			try
			{
				await client.ConnectAsync(IPAddress.Loopback, Port, timeout.Token).ConfigureAwait(false);

				return;
			}
			catch (SocketException)
			{
			}

			await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token).ConfigureAwait(false);
		}
	}

	private static int GetFreePort()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();

		return port;
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}
}

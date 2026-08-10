// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using System.Windows.Automation;

namespace Files.AxeTests;

[TestClass]
public sealed class AssemblyInitializer
{
	private const string DefaultFilesAppId = "FilesDev_ykqwq8d6ps0ag!App";
	private static readonly TimeSpan _launchTimeout = TimeSpan.FromSeconds(45);
	private static Process? _applicationProcess;
	private static AutomationElement? _rootElement;

	public static bool HasSession => _applicationProcess is not null && !_applicationProcess.HasExited && _rootElement is not null;

	public static Process ApplicationProcess => _applicationProcess ?? throw new InvalidOperationException("The Files application process has not been initialized.");

	public static AutomationElement RootElement => _rootElement ?? throw new InvalidOperationException("The Files automation root has not been initialized.");

	[AssemblyInitialize]
	public static void CreateSession(TestContext _)
	{
		if (HasSession)
		{
			return;
		}

		var existingProcessIds = Process.GetProcessesByName("Files").Select(static process => process.Id).ToHashSet();
		foreach (var appId in GetFilesAppIds())
		{
			using var launcher = Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{appId}") { UseShellExecute = true });
			_applicationProcess = WaitForApplicationProcess(existingProcessIds, _launchTimeout);
			if (_applicationProcess is not null)
			{
				break;
			}
		}

		if (_applicationProcess is null)
		{
			throw new AssertFailedException($"Files did not launch within {_launchTimeout}. Registered app IDs tried: {string.Join(", ", GetFilesAppIds())}.");
		}

		_rootElement = AutomationElement.FromHandle(_applicationProcess.MainWindowHandle);
		if (_rootElement.TryGetCurrentPattern(WindowPattern.Pattern, out var pattern) && pattern is WindowPattern windowPattern)
		{
			try
			{
				windowPattern.SetWindowVisualState(WindowVisualState.Maximized);
			}
			catch (InvalidOperationException)
			{
				// The CI desktop can reject window resizing while it is still settling.
			}
		}

		TestHelper.WaitForElementByAutomationId("PathTextBox", _launchTimeout);
	}

	[AssemblyCleanup]
	public static void TestRunTearDown()
	{
		TearDown();
	}

	public static void TearDown()
	{
		_rootElement = null;
		if (_applicationProcess is null)
		{
			return;
		}

		try
		{
			if (!_applicationProcess.HasExited)
			{
				_applicationProcess.CloseMainWindow();
				if (!_applicationProcess.WaitForExit(10_000))
				{
					_applicationProcess.Kill(entireProcessTree: true);
					_applicationProcess.WaitForExit(5_000);
				}
			}
		}
		catch (InvalidOperationException)
		{
		}
		finally
		{
			_applicationProcess.Dispose();
			_applicationProcess = null;
		}
	}

	private static Process? WaitForApplicationProcess(IReadOnlySet<int> existingProcessIds, TimeSpan timeout)
	{
		var stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < timeout)
		{
			var process = Process.GetProcessesByName("Files").FirstOrDefault(candidate => !existingProcessIds.Contains(candidate.Id) && !candidate.HasExited && candidate.MainWindowHandle != IntPtr.Zero);
			if (process is not null)
			{
				return process;
			}

			Thread.Sleep(100);
		}

		return null;
	}

	private static IReadOnlyList<string> GetFilesAppIds()
	{
		var configuredAppIds = Environment.GetEnvironmentVariable("FILES_AXE_TEST_APP_ID");
		if (string.IsNullOrWhiteSpace(configuredAppIds))
		{
			return [DefaultFilesAppId];
		}

		return configuredAppIds.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}
}

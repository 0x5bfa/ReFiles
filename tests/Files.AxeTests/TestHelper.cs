// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using System.IO;
using System.Windows.Automation;

namespace Files.AxeTests;

/// <summary>
/// Provides shared UI automation helpers for Axe tests.
/// </summary>
public static class TestHelper
{
	/// <summary>
	/// Waits for a descendant automation element with the specified automation ID.
	/// </summary>
	/// <param name="automationId">The automation ID to find.</param>
	/// <param name="timeout">The maximum time to wait.</param>
	/// <returns>The matching automation element.</returns>
	public static AutomationElement WaitForElementByAutomationId(string automationId, TimeSpan timeout)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(automationId);

		var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, automationId);

		return WaitForElement(AssemblyInitializer.RootElement, condition, automationId, timeout);
	}

	/// <summary>
	/// Enters a path in the path text box and invokes navigation.
	/// </summary>
	/// <param name="pathTextBox">The path text box automation element, updated if it becomes unavailable.</param>
	/// <param name="path">The path to enter.</param>
	public static void EnterPath(ref AutomationElement pathTextBox, string path)
	{
		ArgumentNullException.ThrowIfNull(pathTextBox);

		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		try
		{
			SetPathAndNavigate(pathTextBox, path);
		}
		catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException)
		{
			pathTextBox = WaitForElementByAutomationId("PathTextBox", TimeSpan.FromSeconds(15));
			SetPathAndNavigate(pathTextBox, path);
		}
	}

	/// <summary>
	/// Waits until the Files application displays the expected path.
	/// </summary>
	/// <param name="expectedPath">The path expected in the path text box.</param>
	/// <param name="timeout">The maximum time to wait.</param>
	public static void WaitForPath(string expectedPath, TimeSpan timeout)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(expectedPath);

		var stopwatch = Stopwatch.StartNew();
		string? currentPath = null;
		Exception? lastException = null;
		while (stopwatch.Elapsed < timeout)
		{
			try
			{
				var pathTextBox = WaitForElementByAutomationId("PathTextBox", TimeSpan.FromSeconds(2));
				currentPath = GetValuePattern(pathTextBox).Current.Value;
				if (PathsEqual(currentPath, expectedPath))
				{
					return;
				}
			}
			catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException or AssertFailedException)
			{
				lastException = exception;
			}

			Thread.Sleep(100);
		}

		throw new AssertFailedException($"Path '{expectedPath}' was not reached within {timeout}. Current path: '{currentPath}'. Last automation error: {lastException?.Message}");
	}

	/// <summary>
	/// Asserts that the Files application process and path text box are responsive.
	/// </summary>
	public static void AssertApplicationResponsive()
	{
		var process = AssemblyInitializer.ApplicationProcess;
		process.Refresh();
		Assert.IsFalse(process.HasExited, "The Files application process exited during navigation.");
		Assert.IsTrue(process.Responding, "The Files application process is not responding.");
		var pathTextBox = WaitForElementByAutomationId("PathTextBox", TimeSpan.FromSeconds(15));
		Assert.IsTrue(pathTextBox.Current.IsEnabled, "The path text box is not enabled.");
	}

	private static AutomationElement WaitForElement(AutomationElement root, Condition condition, string description, TimeSpan timeout)
	{
		var stopwatch = Stopwatch.StartNew();
		Exception? lastException = null;
		while (stopwatch.Elapsed < timeout)
		{
			try
			{
				var element = root.FindFirst(TreeScope.Descendants, condition);
				if (element is not null)
				{
					return element;
				}
			}
			catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException)
			{
				lastException = exception;
			}

			Thread.Sleep(100);
		}

		throw new AssertFailedException($"UI element '{description}' was not available within {timeout}. Last automation error: {lastException?.Message}");
	}

	private static ValuePattern GetValuePattern(AutomationElement element)
	{
		if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) || pattern is not ValuePattern valuePattern)
		{
			throw new InvalidOperationException("The path text box does not support the Value pattern.");
		}

		return valuePattern;
	}

	private static void SetPathAndNavigate(AutomationElement pathTextBox, string path)
	{
		GetValuePattern(pathTextBox).SetValue(path);
		pathTextBox.SetFocus();
		System.Windows.Forms.SendKeys.SendWait("{ENTER}");
	}

	private static bool PathsEqual(string? actualPath, string expectedPath)
	{
		if (string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (string.IsNullOrWhiteSpace(actualPath))
		{
			return false;
		}

		var normalizedActualPath = Path.TrimEndingDirectorySeparator(actualPath).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
		var normalizedExpectedPath = Path.TrimEndingDirectorySeparator(expectedPath).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

		return string.Equals(normalizedActualPath, normalizedExpectedPath, StringComparison.OrdinalIgnoreCase);
	}
}

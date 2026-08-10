// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Diagnostics;
using System.IO;
using System.Windows.Automation;

namespace Files.AxeTests.Helper;

public static class TestHelper
{
	public static AutomationElement WaitForElementByAutomationId(string automationId, TimeSpan timeout)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(automationId);

		var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, automationId);

		return WaitForElement(SessionManager.RootElement, condition, automationId, timeout);
	}

	public static void EnterPath(ref AutomationElement pathTextBox, ref AutomationElement navigatePathButton, string path)
	{
		ArgumentNullException.ThrowIfNull(pathTextBox);

		ArgumentNullException.ThrowIfNull(navigatePathButton);

		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		try
		{
			SetPathAndNavigate(pathTextBox, navigatePathButton, path);
		}
		catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException)
		{
			pathTextBox = WaitForElementByAutomationId("PathTextBox", TimeSpan.FromSeconds(15));
			navigatePathButton = WaitForElementByAutomationId("NavigatePathButton", TimeSpan.FromSeconds(15));
			SetPathAndNavigate(pathTextBox, navigatePathButton, path);
		}
	}

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

	public static void AssertApplicationResponsive()
	{
		var process = SessionManager.ApplicationProcess;
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

	private static InvokePattern GetInvokePattern(AutomationElement element)
	{
		if (!element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern) || pattern is not InvokePattern invokePattern)
		{
			throw new InvalidOperationException($"The UI element '{element.Current.AutomationId}' does not support the Invoke pattern.");
		}

		return invokePattern;
	}

	private static void SetPathAndNavigate(AutomationElement pathTextBox, AutomationElement navigatePathButton, string path)
	{
		GetValuePattern(pathTextBox).SetValue(path);
		GetInvokePattern(navigatePathButton).Invoke();
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

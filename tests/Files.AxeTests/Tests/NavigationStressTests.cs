// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Windows.Automation;

namespace Files.AxeTests.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NavigationStressTests
{
	private const int DefaultIterationCount = 250;
	private const int MaximumIterationCount = 1_000;
	private static readonly TimeSpan _navigationTimeout = TimeSpan.FromSeconds(45);
	private static readonly TimeSpan _postNavigationDelay = TimeSpan.FromSeconds(3);

	private static int IterationCount => GetIterationCount();

	[TestInitialize]
	public void Initialize()
	{
		TestHelper.AssertApplicationResponsive();
	}

	[TestMethod]
	public void RepeatedlyEnteringSamePathKeepsApplicationResponsive()
	{
		var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
		Assert.IsTrue(Directory.Exists(windowsPath), $"The Windows directory '{windowsPath}' does not exist.");

		var pathTextBox = TestHelper.WaitForElementByAutomationId("PathTextBox", _navigationTimeout);
		var navigatePathButton = TestHelper.WaitForElementByAutomationId("NavigatePathButton", _navigationTimeout);
		for (var iteration = 0; iteration < IterationCount; iteration++)
		{
			TestHelper.EnterPath(ref pathTextBox, ref navigatePathButton, windowsPath);
		}

		Thread.Sleep(_postNavigationDelay);
		TestHelper.WaitForPath(windowsPath, _navigationTimeout);
		TestHelper.AssertApplicationResponsive();
	}

	[TestMethod]
	public void RapidlyEnteringDifferentPathsKeepsApplicationResponsive()
	{
		var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
		var systemPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
		Assert.IsTrue(Directory.Exists(windowsPath), $"The Windows directory '{windowsPath}' does not exist.");
		Assert.IsTrue(Directory.Exists(systemPath), $"The system directory '{systemPath}' does not exist.");

		var paths = new[] { windowsPath, systemPath };
		var pathTextBox = TestHelper.WaitForElementByAutomationId("PathTextBox", _navigationTimeout);
		var navigatePathButton = TestHelper.WaitForElementByAutomationId("NavigatePathButton", _navigationTimeout);
		for (var iteration = 0; iteration < IterationCount; iteration++)
		{
			TestHelper.EnterPath(ref pathTextBox, ref navigatePathButton, paths[iteration % paths.Length]);
		}

		var expectedPath = paths[(IterationCount - 1) % paths.Length];
		Thread.Sleep(_postNavigationDelay);
		TestHelper.WaitForPath(expectedPath, _navigationTimeout);
		TestHelper.AssertApplicationResponsive();
	}

	private static int GetIterationCount()
	{
		var configuredValue = Environment.GetEnvironmentVariable("FILES_NAVIGATION_STRESS_ITERATIONS");
		if (string.IsNullOrWhiteSpace(configuredValue))
		{
			return DefaultIterationCount;
		}

		if (!int.TryParse(configuredValue, out var iterationCount) || iterationCount < 1 || iterationCount > MaximumIterationCount)
		{
			throw new InvalidOperationException($"FILES_NAVIGATION_STRESS_ITERATIONS must be between 1 and {MaximumIterationCount}.");
		}

		return iterationCount;
	}
}

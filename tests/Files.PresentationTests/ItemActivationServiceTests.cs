// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Activation;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.PresentationTests;

[TestClass]
public sealed class ItemActivationServiceTests
{
	private static readonly StorageSourceId _windowsSourceId = new(WindowsStorageSource.DefaultSourceType);

	[TestMethod]
	public async Task NonWindowsFolderNavigatesWithoutShellInvocation()
	{
		var shell = new FakeWindowsShellDefaultCommandService(new ShellDefaultCommand("properties"));
		var service = new ItemActivationService(_windowsSourceId, 42, shell);
		var request = new ItemActivationRequest(CreateReference(new StorageSourceId("test"), "test", "item"), true, null, null);

		var result = await service.ActivateAsync(request);

		Assert.AreEqual(ItemActivationOutcome.Navigate, result);
		Assert.AreEqual(0, shell.GetDefaultCommandCallCount);
		Assert.AreEqual(0, shell.InvokeCallCount);
	}

	[TestMethod]
	public async Task WindowsFolderWithOpenVerbNavigatesInApp()
	{
		var shell = new FakeWindowsShellDefaultCommandService(new ShellDefaultCommand("open"));
		var service = new ItemActivationService(_windowsSourceId, 42, shell);
		var request = new ItemActivationRequest(CreateReference(_windowsSourceId, WindowsStorageSource.ShellAddressScheme, "shell:item"), true, null, null);

		var result = await service.ActivateAsync(request);

		Assert.AreEqual(ItemActivationOutcome.Navigate, result);
		Assert.AreEqual(1, shell.GetDefaultCommandCallCount);
		Assert.AreEqual(0, shell.InvokeCallCount);
	}

	[TestMethod]
	public async Task WindowsFolderWithPropertiesVerbInvokesShellDefault()
	{
		var shell = new FakeWindowsShellDefaultCommandService(new ShellDefaultCommand("properties"));
		var service = new ItemActivationService(_windowsSourceId, 42, shell);
		var invocationPoint = new WindowsShellInvocationPoint(10, 20);
		var request = new ItemActivationRequest(CreateReference(_windowsSourceId, WindowsStorageSource.ShellAddressScheme, "shell:recycle-item"), true, null, invocationPoint);

		var result = await service.ActivateAsync(request);

		Assert.AreEqual(ItemActivationOutcome.Invoked, result);
		Assert.AreEqual(1, shell.InvokeCallCount);
		Assert.AreEqual(42, shell.LastContext?.OwnerWindowHandle);
		Assert.AreEqual(10, shell.LastContext?.InvocationPoint?.X);
		Assert.AreEqual(20, shell.LastContext?.InvocationPoint?.Y);
	}

	[TestMethod]
	public async Task WindowsFileInvokesShellDefaultWithoutProbingVerb()
	{
		var shell = new FakeWindowsShellDefaultCommandService(new ShellDefaultCommand("open"));
		var service = new ItemActivationService(_windowsSourceId, 42, shell);
		var request = new ItemActivationRequest(CreateReference(_windowsSourceId, WindowsStorageSource.FileAddressScheme, @"C:\folder\file.txt"), false, @"C:\folder", null);

		var result = await service.ActivateAsync(request);

		Assert.AreEqual(ItemActivationOutcome.Invoked, result);
		Assert.AreEqual(0, shell.GetDefaultCommandCallCount);
		Assert.AreEqual(1, shell.InvokeCallCount);
		Assert.AreEqual(@"C:\folder", shell.LastContext?.WorkingDirectory);
	}

	[TestMethod]
	public async Task FileSystemFolderWithoutCanonicalVerbNavigatesInApp()
	{
		var shell = new FakeWindowsShellDefaultCommandService(new ShellDefaultCommand(null));
		var service = new ItemActivationService(_windowsSourceId, 42, shell);
		var request = new ItemActivationRequest(CreateReference(_windowsSourceId, WindowsStorageSource.FileAddressScheme, @"C:\folder"), true, null, null);

		var result = await service.ActivateAsync(request);

		Assert.AreEqual(ItemActivationOutcome.Navigate, result);
		Assert.AreEqual(0, shell.InvokeCallCount);
	}

	[TestMethod]
	public async Task FileSystemFolderWithoutDefaultCommandNavigatesInApp()
	{
		var shell = new FakeWindowsShellDefaultCommandService(null);
		var service = new ItemActivationService(_windowsSourceId, 42, shell);
		var request = new ItemActivationRequest(CreateReference(_windowsSourceId, WindowsStorageSource.FileAddressScheme, @"C:\folder"), true, null, null);

		var result = await service.ActivateAsync(request);

		Assert.AreEqual(ItemActivationOutcome.Navigate, result);
		Assert.AreEqual(0, shell.InvokeCallCount);
	}

	private static StorableReference CreateReference(StorageSourceId sourceId, string scheme, string value)
	{
		return new StorableReference(sourceId, value, new StorageAddress(scheme, value));
	}

	private sealed class FakeWindowsShellDefaultCommandService(ShellDefaultCommand? defaultCommand, bool invokeResult = true) : IWindowsShellDefaultCommandService
	{
		public int GetDefaultCommandCallCount { get; private set; }

		public int InvokeCallCount { get; private set; }

		public WindowsShellInvocationContext? LastContext { get; private set; }

		public Task<ShellDefaultCommand?> GetDefaultCommandAsync(StorableReference reference, CancellationToken cancellationToken = default)
		{
			GetDefaultCommandCallCount++;

			return Task.FromResult(defaultCommand);
		}

		public Task<bool> InvokeDefaultCommandAsync(StorableReference reference, WindowsShellInvocationContext context, CancellationToken cancellationToken = default)
		{
			InvokeCallCount++;
			LastContext = context;

			return Task.FromResult(invokeResult);
		}
	}
}

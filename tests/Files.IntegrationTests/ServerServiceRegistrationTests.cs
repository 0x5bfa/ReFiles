// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.BackgroundServer;
using Files.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Files.IntegrationTests;

/// <summary>Verifies process ownership of server storage and activity services.</summary>
[TestClass]
public sealed class ServerServiceRegistrationTests
{
	/// <summary>Disposing one process root releases its storage without affecting another root.</summary>
	[TestMethod]
	public async Task RootsOwnIndependentStorageAndLifetimes()
	{
		await using var first = ServerServiceRegistration.CreateServices().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
		await using var second = ServerServiceRegistration.CreateServices().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
		var firstLifetime = first.GetRequiredService<ServerLifetime>();
		var secondLifetime = second.GetRequiredService<ServerLifetime>();
		var firstStorage = first.GetRequiredService<StorageOperationExecutor>();
		var secondStorage = second.GetRequiredService<StorageOperationExecutor>();
		var firstSource = firstStorage.Source;
		Assert.AreNotSame(firstSource, secondStorage.Source);
		await first.DisposeAsync();
		Assert.Throws<ObjectDisposedException>(() => firstLifetime.Acquire());
		await Assert.ThrowsAsync<ObjectDisposedException>(() => firstSource.ResolveAsync(new StorageAddress("file", Environment.CurrentDirectory)).AsTask());
		Assert.IsTrue(firstLifetime.ExitTask.IsCompleted);
		Assert.IsFalse(secondLifetime.ExitTask.IsCompleted);
		using var lease = secondLifetime.Acquire(isClient: true);
		var item = await secondStorage.Source.ResolveAsync(new StorageAddress("file", Environment.CurrentDirectory));
		Assert.IsNotNull(item);
		Assert.IsTrue(secondLifetime.HasClients);
	}
}

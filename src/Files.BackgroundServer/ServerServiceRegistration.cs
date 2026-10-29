// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using Files.Core.Capabilities.Thumbnails;
using Microsoft.Extensions.DependencyInjection;

namespace Files.BackgroundServer;

internal static class ServerServiceRegistration
{
	internal static IServiceCollection CreateServices()
	{
		var services = new ServiceCollection();
		services.AddSingleton(_ => new ServerLifetime(TimeSpan.FromSeconds(30)));
		services.AddSingleton(provider => new OperationHost(provider.GetRequiredService<ServerLifetime>()));
		services.AddSingleton(_ => new StorageOperationExecutor());
		services.AddSingleton(_ => new MemoryThumbnailCache());
		services.AddSingleton(provider => new ShellNewMenuCache(provider.GetRequiredService<StorageOperationExecutor>()));

		return services;
	}
}

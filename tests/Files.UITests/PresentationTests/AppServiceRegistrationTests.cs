// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core;
using Files.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.UITests;

/// <summary>Verifies the lifetime boundary between the UI host and Core.</summary>
[TestClass]
public sealed class AppServiceRegistrationTests
{
	/// <summary>Core borrows host logging; only root disposal releases the factory.</summary>
	[TestMethod]
	public async Task RuntimeDisposalKeepsHostLoggingAlive()
	{
		var factory = new TrackingLoggerFactory();
		var registrations = AppServiceRegistration.CreateServices();
		registrations.Replace(ServiceDescriptor.Singleton<ILoggerFactory>(_ => factory));
		await using var services = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
		var loggerFactory = services.GetRequiredService<ILoggerFactory>();
		var logger = services.GetRequiredService<ILogger<AppServiceRegistrationTests>>();
		await using (var runtime = new FilesCoreBuilder(loggerFactory: loggerFactory).Build())
		{
			Assert.AreEqual(0, factory.DisposeCount);
		}

		logger.LogInformation("Core shutdown completed.");
		loggerFactory.CreateLogger("AfterCoreShutdown");
		Assert.AreEqual(0, factory.DisposeCount);
		Assert.IsTrue(factory.CreatedCategories.Contains(typeof(AppServiceRegistrationTests).FullName!));
		await services.DisposeAsync();
		Assert.AreEqual(1, factory.DisposeCount);
	}

	private sealed class TrackingLoggerFactory : ILoggerFactory
	{
		public int DisposeCount { get; private set; }
		public List<string> CreatedCategories { get; } = [];

		public void AddProvider(ILoggerProvider provider)
		{
		}

		public ILogger CreateLogger(string categoryName)
		{
			ObjectDisposedException.ThrowIf(DisposeCount > 0, this);

			CreatedCategories.Add(categoryName);

			return NullLogger.Instance;
		}

		public void Dispose()
		{
			DisposeCount++;
		}
	}
}

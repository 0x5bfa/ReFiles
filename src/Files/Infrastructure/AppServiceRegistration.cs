// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Commands;
using Files.Settings;
using Files.StorageOperations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System.IO;

namespace Files.Infrastructure;

internal static class AppServiceRegistration
{
	internal static IServiceCollection CreateServices()
	{
		var services = new ServiceCollection();
		services.AddSingleton<ILoggerFactory>(_ => CreateLoggerFactory());
		services.AddLogging();
		services.AddSingleton(provider => new WinRTStorageOperationService(provider.GetRequiredService<ILogger<WinRTStorageOperationService>>()));
		services.AddSingleton(provider => new StorageOperationTracker(provider.GetRequiredService<WinRTStorageOperationService>(), provider.GetRequiredService<ILogger<StorageOperationTracker>>()));
		services.AddSingleton(_ => AppCommandRegistration.Build());

		return services;
	}

	private static ILoggerFactory CreateLoggerFactory()
	{
		var logFilePath = Path.Combine(AppSettingsService.GetDefaultLocalFolderPath(), "Logs", "Files-.log");
		var serilogLogger = new LoggerConfiguration()
			.MinimumLevel.Debug()
			.WriteTo.Async(writeTo => writeTo.File(logFilePath, rollingInterval: RollingInterval.Day, fileSizeLimitBytes: 10 * 1024 * 1024, rollOnFileSizeLimit: true,
				retainedFileCountLimit: 10, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"))
			.CreateLogger();

		return LoggerFactory.Create(builder =>
		{
			builder.AddDebug();
			builder.AddSerilog(serilogLogger, dispose: true);
			builder.SetMinimumLevel(LogLevel.Debug);
		});
	}
}

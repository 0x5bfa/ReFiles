// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Files.Core.Capabilities.Thumbnails;
using Microsoft.Extensions.DependencyInjection;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.WinRT;

namespace Files.BackgroundServer;

class Program
{
	// The WinRT activation constructor borrows these services from the process root.
	internal static ServerLifetime Lifetime { get; private set; } = null!;
	internal static OperationHost Operations { get; private set; } = null!;
	internal static StorageOperationExecutor Storage { get; private set; } = null!;
	internal static MemoryThumbnailCache Thumbnails { get; private set; } = null!;
	internal static ShellNewMenuCache ShellNewItems { get; private set; } = null!;

	static void Main()
	{
		AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;

		ServiceProvider? services = null;
		PInvoke.RoInitialize(RO_INIT_TYPE.RO_INIT_MULTITHREADED).ThrowOnFailure();
		try
		{
			services = ServerServiceRegistration.CreateServices().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
			Lifetime = services.GetRequiredService<ServerLifetime>();
			Operations = services.GetRequiredService<OperationHost>();
			using (Lifetime.Acquire())
			{
				Storage = services.GetRequiredService<StorageOperationExecutor>();
				Thumbnails = services.GetRequiredService<MemoryThumbnailCache>();
				ShellNewItems = services.GetRequiredService<ShellNewMenuCache>();
				BackgroundServerHost.Initialize();
			}

			var className = typeof(AppService).FullName!;
			PInvoke.WindowsCreateString(className, (uint)className.Length, out var classId).ThrowOnFailure();
			using (classId)
			{
				RoRevokeActivationFactoriesSafeHandle cookie;
				unsafe
				{
					HSTRING[] classIds = [new(classId.DangerousGetHandle())];
					delegate* unmanaged[Stdcall]<HSTRING, IActivationFactory_unmanaged**, HRESULT> callback = &Helpers.GetActivationFactory;
					PInvoke.RoRegisterActivationFactories(classIds, &callback, out cookie).ThrowOnFailure();
				}

				try
				{
					Lifetime.ExitTask.GetAwaiter().GetResult();
				}
				finally
				{
					cookie.Dispose();
				}
			}
		}
		finally
		{
			try
			{
				BackgroundServerHost.ShutdownAsync().GetAwaiter().GetResult();
			}
			finally
			{
				try
				{
					if (services is not null)
					{
						services.DisposeAsync().AsTask().GetAwaiter().GetResult();
					}
				}
				finally
				{
					PInvoke.RoUninitialize();
				}
			}
		}
	}

	private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
	{
		Debug.WriteLine($"{DateTime.Now}|{e.Exception}");
	}
}

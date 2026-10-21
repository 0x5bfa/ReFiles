// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.WinRT;

namespace Files.Operations;

class Program
{
	internal static readonly ServerLifetime Lifetime = new(TimeSpan.FromSeconds(30));
	internal static readonly OperationHost Operations = new(Lifetime);
	internal static readonly Lazy<StorageOperationExecutor> Storage = new(() => new StorageOperationExecutor());

	static void Main()
	{
		AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;

		PInvoke.RoInitialize(RO_INIT_TYPE.RO_INIT_MULTITHREADED).ThrowOnFailure();
		try
		{
			var className = typeof(AppService).FullName!;
			PInvoke.WindowsCreateString(className, (uint)className.Length, out var classId).ThrowOnFailure();
			using (classId)
			{
				RO_REGISTRATION_COOKIE cookie;
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
					PInvoke.RoRevokeActivationFactories(cookie);
				}
			}
		}
		finally
		{
			Lifetime.Dispose();
			if (Storage.IsValueCreated)
			{
				Storage.Value.DisposeAsync().AsTask().GetAwaiter().GetResult();
			}

			PInvoke.RoUninitialize();
		}
	}

	private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
	{
		Debug.WriteLine($"{DateTime.Now}|{e.Exception}");
	}
}

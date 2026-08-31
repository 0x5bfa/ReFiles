// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.WinRT;

namespace Files.Operations;

internal static class Program
{
	private static readonly CancellationTokenSource _cancellationTokenSource = new();

	internal static TaskCompletionSource<bool> ExitSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

	private static async Task Main()
	{
		RO_REGISTRATION_COOKIE cookie = default;
		var initializeResult = PInvoke.RoInitialize(RO_INIT_TYPE.RO_INIT_MULTITHREADED);
		initializeResult.ThrowOnFailure();

		var classIdHandles = new List<WindowsDeleteStringSafeHandle>();

		try
		{
			var classNames = typeof(Program).Assembly.GetTypes()
				.Where(t => t.IsSealed && t.IsPublic && t.IsClass)
				.Select(t => t.FullName!)
				.Where(name => name.StartsWith("Files.Operations.", StringComparison.Ordinal))
				.ToArray();
			classIdHandles.Capacity = classNames.Length;

			foreach (var className in classNames)
			{
				var createStringResult = PInvoke.WindowsCreateString(className, checked((uint)className.Length), out var classId);
				if (createStringResult.Failed)
				{
					classId.Dispose();
					createStringResult.ThrowOnFailure();
				}

				classIdHandles.Add(classId);
			}

			var classIds = classIdHandles.Select(static classId => new HSTRING(classId.DangerousGetHandle())).ToArray();

			unsafe
			{
				delegate* unmanaged[Stdcall]<HSTRING, IActivationFactory_unmanaged**, HRESULT>[] callbacks = new delegate* unmanaged[Stdcall]<HSTRING, IActivationFactory_unmanaged**, HRESULT>[classIds.Length];
				for (int index = 0; index < callbacks.Length; index++)
				{
					callbacks[index] = &Helpers.GetActivationFactory;
				}

				fixed (delegate* unmanaged[Stdcall]<HSTRING, IActivationFactory_unmanaged**, HRESULT>* pCallbacks = callbacks)
				{
					var registerResult = PInvoke.RoRegisterActivationFactories(classIds, pCallbacks, out cookie);
					registerResult.ThrowOnFailure();
				}
			}

			AppDomain.CurrentDomain.ProcessExit += (_, _) => _cancellationTokenSource.Cancel();

			try
			{
				await ExitSignal.Task.WaitAsync(_cancellationTokenSource.Token);
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
		finally
		{
			if (cookie != 0)
			{
				PInvoke.RoRevokeActivationFactories(cookie);
			}

			foreach (var classId in classIdHandles)
			{
				classId.Dispose();
			}

			PInvoke.RoUninitialize();
		}
	}
}

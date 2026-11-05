// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Windows;
using Windows.Win32;
using Windows.Win32.System.WinRT;

namespace Files.UnitTests;

/// <summary>Verifies source-generated Windows Runtime inspection and native buffer ownership.</summary>
[TestClass]
public sealed class WindowsInspectableInteropTests
{
	/// <summary>Reads a real WinRT factory's IID array through an owned native buffer.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task GetIidsReturnsAnOwnedReadableArray()
	{
		await using var scheduler = new WindowsShellScheduler();
		var identifiers = await scheduler.InvokeAsync(ReadFactoryInterfaceIds);
		Assert.IsNotEmpty(identifiers);
		Assert.IsTrue(identifiers.All(identifier => identifier != Guid.Empty));
	}

	private static unsafe Guid[] ReadFactoryInterfaceIds()
	{
		const string runtimeClassName = "Windows.Foundation.Uri";
		var hr = PInvoke.WindowsCreateString(runtimeClassName, checked((uint)runtimeClassName.Length), out var className);
		hr.ThrowOnFailure();

		using (className)
		{
			hr = PInvoke.RoGetActivationFactory(className, out IInspectable factory);
			hr.ThrowOnFailure();
			hr = factory.GetIids(out var count, out var identifiers);
			try
			{
				hr.ThrowOnFailure();
				Assert.IsTrue(identifiers != null);

				return new ReadOnlySpan<Guid>(identifiers, checked((int)count)).ToArray();
			}
			finally
			{
				PInvoke.CoTaskMemFree(identifiers);
			}
		}
	}
}

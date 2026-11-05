// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Reflection;
using Files.Core.Storage.Windows;

namespace Files.UnitTests;

/// <summary>Verifies that provider consumers never depend on generated native declarations.</summary>
[TestClass]
public sealed class WindowsNativeBoundaryTests
{
	/// <summary>Checks exported types and every public or protected member signature for native dependencies.</summary>
	[TestMethod]
	public void ProviderPublicSurfaceDoesNotExposeNativeTypes()
	{
		const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
		var assembly = typeof(WindowsStorageSource).Assembly;
		foreach (var type in assembly.GetExportedTypes())
		{
			AssertManagedType(type);
			if (type.BaseType is { } baseType)
			{
				AssertManagedType(baseType);
			}

			foreach (var contract in type.GetInterfaces())
			{
				AssertManagedType(contract);
			}

			foreach (var method in type.GetMethods(Flags).Where(static method => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly))
			{
				AssertManagedType(method.ReturnType);
				AssertParameters(method);
				foreach (var argument in method.GetGenericArguments())
				{
					foreach (var constraint in argument.GetGenericParameterConstraints())
					{
						AssertManagedType(constraint);
					}
				}
			}

			foreach (var constructor in type.GetConstructors(Flags).Where(static constructor => constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly))
			{
				AssertParameters(constructor);
			}

			foreach (var field in type.GetFields(Flags).Where(static field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly))
			{
				AssertManagedType(field.FieldType);
			}
		}
	}

	/// <summary>Preserves successful cancellation statuses and the original failure code.</summary>
	[TestMethod]
	public void ShellOperationResultPreservesSuccessAndFailureStatuses()
	{
		var cancelled = new WindowsShellOperationResult(1);
		Assert.IsTrue(cancelled.Succeeded);
		Assert.IsFalse(cancelled.Failed);
		cancelled.ThrowOnFailure();
		var failed = new WindowsShellOperationResult(unchecked((int)0x80004005));
		Assert.IsFalse(failed.Succeeded);
		Assert.IsTrue(failed.Failed);
		var exception = Assert.Throws<System.Runtime.InteropServices.COMException>(failed.ThrowOnFailure);
		Assert.AreEqual(failed.HResult, exception.HResult);
	}

	private static void AssertParameters(MethodBase method)
	{
		foreach (var parameter in method.GetParameters())
		{
			AssertManagedType(parameter.ParameterType);
		}
	}

	private static void AssertManagedType(Type type)
	{
		Assert.IsFalse(type.Namespace?.StartsWith("Windows.Win32", StringComparison.Ordinal) is true, type.FullName);
		if (type.GetElementType() is { } element)
		{
			AssertManagedType(element);
		}

		foreach (var argument in type.GetGenericArguments())
		{
			if (!argument.IsGenericParameter)
			{
				AssertManagedType(argument);
			}
		}
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Reflection;
using Files.Core;
using Files.Core.Capabilities;
using Files.Core.Data;
using Files.Core.Sessions;
using Files.Core.Storage;
using Files.Hosting;

namespace Files.UnitTests;

/// <summary>Verifies consumer API closure and process-host ownership boundaries.</summary>
[TestClass]
public sealed class ConsumerContractBoundaryTests
{
	/// <summary>Contracts have no implementation assembly references and neither public surface leaks implementation types.</summary>
	[TestMethod]
	public void PublicContractsAndHostingDoNotExposeImplementations()
	{
		var contracts = typeof(ICapability).Assembly;
		Assert.AreEqual("Files.Core.Contracts", contracts.GetName().Name);
		foreach (var dependency in contracts.GetReferencedAssemblies())
		{
			Assert.IsFalse(IsImplementation(dependency.Name!), dependency.FullName);
		}

		AssertPublicSurface(contracts);
		AssertPublicSurface(typeof(FilesHost).Assembly);
		Assert.IsNull(contracts.GetType("Files.Core.Capabilities.CapabilityBuilder"));
		Assert.IsNull(contracts.GetType("Files.Core.Capabilities.ItemContext"));
		Assert.IsNull(contracts.GetType("Files.Core.Storage.IStorageSource"));
	}

	/// <summary>A UI host exposes descriptors and consumer sessions without source administration or operation-host authority.</summary>
	/// <returns>The asynchronous regression test.</returns>
	[TestMethod]
	public async Task UiRuntimeExposesConsumerViewsAndRetainsOwnership()
	{
		await using var runtime = FilesHost.CreateUiRuntime(operations: new StorageOperationService([]));
		Assert.IsFalse(runtime is IOperationHostRuntime);
		Assert.IsFalse(runtime is FilesCoreRuntime);
		Assert.IsFalse(runtime.Workspace is IStorageSourceRegistry);
		Assert.IsFalse(runtime.Workspace is IStorageWorkspace);
		Assert.IsFalse(runtime.Workspace is IDisposable);
		Assert.IsFalse(runtime.Workspace is IAsyncDisposable);
		Assert.HasCount(1, runtime.Workspace.Sources);
		Assert.AreEqual("windows-shell", runtime.Workspace.Sources[0].SourceType);
		await using var pane = runtime.CreateBrowsePane();
		Assert.IsInstanceOfType<IBrowsePaneSession>(pane.Content);
		await using var settings = runtime.CreateSettingsPane();
		Assert.IsInstanceOfType<ISettingsPaneSession>(settings.Content);
	}

	private static void AssertPublicSurface(Assembly assembly)
	{
		const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
		foreach (var type in assembly.GetExportedTypes())
		{
			if (type.BaseType is { } baseType)
			{
				AssertConsumerType(baseType);
			}

			foreach (var contract in type.GetInterfaces())
			{
				AssertConsumerType(contract);
			}

			foreach (var method in type.GetMethods(Flags).Where(static method => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly))
			{
				AssertConsumerType(method.ReturnType);
				foreach (var parameter in method.GetParameters())
				{
					AssertConsumerType(parameter.ParameterType);
				}
			}

			foreach (var constructor in type.GetConstructors(Flags).Where(static constructor => constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly))
			{
				foreach (var parameter in constructor.GetParameters())
				{
					AssertConsumerType(parameter.ParameterType);
				}
			}

			foreach (var field in type.GetFields(Flags).Where(static field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly))
			{
				AssertConsumerType(field.FieldType);
			}
		}
	}

	private static void AssertConsumerType(Type type)
	{
		Assert.IsFalse(IsImplementation(type.Assembly.GetName().Name!), type.FullName);
		Assert.IsFalse(type.Namespace?.StartsWith("Windows.Win32", StringComparison.Ordinal) is true, type.FullName);
		if (type.GetElementType() is { } element)
		{
			AssertConsumerType(element);
		}

		foreach (var argument in type.GetGenericArguments())
		{
			AssertConsumerType(argument);
		}
	}

	private static bool IsImplementation(string assemblyName)
	{
		return assemblyName is "Files.Core" or "FluentFTP" or "SevenZipSharp" or "System.Management";
	}
}

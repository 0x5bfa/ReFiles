// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Composition;
using Files.Core.Storage;
using Files.Core.Windows;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Files.UnitTests;

/// <summary>Contains tests for Windows Shell search behavior.</summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowsShellSearchTests
{
	/// <summary>Test case: the Shell continuation service exposes and updates its cancellation state.</summary>
	[TestMethod]
	public void QueryContinuationServiceReflectsCancellation()
	{
		using var cancellation = new CancellationTokenSource();
		var continuation = new WindowsShellQueryContinue(cancellation.Token);
		var continuationId = typeof(IQueryContinue).GUID;

		Assert.AreEqual(HRESULT.S_OK, continuation.QueryContinue());
		Assert.AreEqual(HRESULT.S_OK, continuation.QueryService(in continuationId, in continuationId, out var service));
		Assert.AreSame(continuation, service);

		cancellation.Cancel();

		Assert.AreEqual(HRESULT.S_FALSE, continuation.QueryContinue());
		var unsupportedId = Guid.Empty;
		Assert.AreEqual(HRESULT.E_NOINTERFACE, continuation.QueryService(in unsupportedId, in continuationId, out service));
		Assert.IsNull(service);
	}

	/// <summary>Test case: the Windows slice opens global and scoped search locations.</summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task WindowsSliceOpensGlobalAndScopedSearchLocations()
	{
		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await using var runtime = new FilesCoreBuilder().AddWindowsStorage(enablePreviews: false, enableArchives: false).Build();

		var globalLocation = new SearchLocation($"unlikely-refiles-test-{Guid.NewGuid():N}");
		await using (var globalContext = await runtime.LocationResolver.OpenAsync(globalLocation, cancellation.Token))
		{
			Assert.AreEqual(globalLocation, globalContext.Location);
			Assert.IsNull(globalContext.LocationModel);
			var parentResolver = Assert.IsInstanceOfType<IBrowseLocationParentResolver>(globalContext);
			Assert.AreEqual(HomeLocation.Instance, await parentResolver.GetParentLocationAsync(cancellation.Token));
		}

		var directoryPath = Path.Combine(Path.GetTempPath(), $"ReFiles.SearchTests.{Guid.NewGuid():N}");
		var filePath = Path.Combine(directoryPath, "windows-shell-search-result.txt");
		Directory.CreateDirectory(directoryPath);
		await File.WriteAllTextAsync(filePath, "search test", cancellation.Token);
		try
		{
			await using var scopeModel = await runtime.Workspace.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, directoryPath), cancellation.Token);
			var scopedLocation = new SearchLocation("System.FileName:windows-shell-search-result.txt", scopeModel.Reference);
			await using var scopedContext = await runtime.LocationResolver.OpenAsync(scopedLocation, cancellation.Token);
			var scopedParentResolver = Assert.IsInstanceOfType<IBrowseLocationParentResolver>(scopedContext);
			var parent = Assert.IsInstanceOfType<FolderLocation>(await scopedParentResolver.GetParentLocationAsync(cancellation.Token));

			Assert.AreEqual(scopedLocation, scopedContext.Location);
			Assert.AreEqual(scopeModel.Reference, parent.Folder);

			var foundMatch = false;
			await foreach (var item in scopedContext.GetItemsAsync(cancellation.Token))
			{
				await using (item)
				{
					foundMatch = string.Equals(filePath, item.Reference.LastKnownAddress?.Value, StringComparison.OrdinalIgnoreCase);
				}

				if (foundMatch)
				{
					break;
				}
			}

			Assert.IsTrue(foundMatch, "The scoped Shell search did not return the matching file.");
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}
}

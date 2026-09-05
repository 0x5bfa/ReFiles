// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Composition;
using Files.Core.Storage;
using Files.Core.Windows;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for windows files core builder behavior.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowsFilesCoreBuilderTests
{
	/// <summary>
	/// Test case: default windows slice builds operations features and previews.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task DefaultWindowsSliceBuildsOperationsFeaturesAndPreviews()
	{
		var runtime = new FilesCoreBuilder()
			.AddWindowsStorage()
			.Build();
		var source = runtime.Workspace.Sources.Single();
		var reference = new StorableReference(source.SourceId, "winfs:v1:00000000:0000000000000000", new StorageAddress(WindowsStorageSource.FileAddressScheme, @"C:\missing.txt"));

		Assert.AreEqual(WindowsStorageSource.DefaultSourceType, source.SourceType);
		Assert.IsNotNull(runtime.WindowsShellPreviewSessions);
		Assert.IsTrue(runtime.StorageOperations.CanHandle(new RenameOperationRequest(reference, "renamed.txt")));

		await runtime.DisposeAsync();
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Windows;

namespace Files.UnitTests;

/// <summary>Verifies Windows Shell operation interruption classification.</summary>
[TestClass]
public sealed class WindowsStorageOperationInterruptionTests
{
	/// <summary>Verifies representative Shell and Win32 failures map to stable interruption kinds.</summary>
	[TestMethod]
	[DataRow(unchecked((int)0x80070020), StorageOperationInterruptionKind.InUse)]
	[DataRow(unchecked((int)0x80270027), StorageOperationInterruptionKind.InUse)]
	[DataRow(unchecked((int)0x80270021), StorageOperationInterruptionKind.AccessDenied)]
	[DataRow(unchecked((int)0x80070005), StorageOperationInterruptionKind.ElevationRequired)]
	[DataRow(unchecked((int)0x80270022), StorageOperationInterruptionKind.ElevationRequired)]
	[DataRow(unchecked((int)0x80070070), StorageOperationInterruptionKind.DiskFull)]
	[DataRow(unchecked((int)0x80270032), StorageOperationInterruptionKind.DiskFull)]
	[DataRow(unchecked((int)0x80070002), StorageOperationInterruptionKind.NotFound)]
	[DataRow(unchecked((int)0x8027003F), StorageOperationInterruptionKind.ReadOnly)]
	[DataRow(unchecked((int)0x80270029), StorageOperationInterruptionKind.NameConflict)]
	[DataRow(unchecked((int)0x80004005), StorageOperationInterruptionKind.Unexpected)]
	public void ClassifiesShellFailures(int errorCode, StorageOperationInterruptionKind expected)
	{
		Assert.AreEqual(expected, WindowsStorageOperationErrorClassifier.Classify(errorCode));
	}

	/// <summary>Verifies a skipped result cannot be confused with failure or a produced item.</summary>
	[TestMethod]
	public void RepresentsSkippedOperationResult()
	{
		var result = new StorageOperationResult(true, null, skipped: true);

		Assert.IsTrue(result.Succeeded);
		Assert.IsTrue(result.Skipped);
		Assert.IsNull(result.ResultItem);
		Assert.IsNull(result.Error);
	}
}

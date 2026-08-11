// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Storage;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for storage identity behavior.
/// </summary>
[TestClass]
public sealed class StorageIdentityTests
{
	/// <summary>
	/// Test case: last known address does not participate in reference identity.
	/// </summary>
	[TestMethod]
	public void LastKnownAddressDoesNotParticipateInReferenceIdentity()
	{
		var sourceId = new StorageSourceId("source");
		var before = new StorableReference(sourceId, "item", new StorageAddress("file", @"C:\before.txt"));
		var after = new StorableReference(sourceId, "item", new StorageAddress("file", @"C:\after.txt"));

		Assert.AreEqual(before, after);
		Assert.AreEqual(before.GetHashCode(), after.GetHashCode());
		Assert.AreEqual(new FolderLocation(before), new FolderLocation(after));
	}

	/// <summary>
	/// Test case: item ids remain opaque and case sensitive.
	/// </summary>
	[TestMethod]
	public void ItemIdsRemainOpaqueAndCaseSensitive()
	{
		var sourceId = new StorageSourceId("source");
		var lower = new StorableReference(sourceId, "item-a");
		var upper = new StorableReference(sourceId, "ITEM-A");

		Assert.AreNotEqual(lower, upper);
	}
}

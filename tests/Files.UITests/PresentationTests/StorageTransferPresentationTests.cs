// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Storage.Archives;
using Files.StorageOperations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.UITests.PresentationTests;

/// <summary>Protects transfer value projection and authoritative partial-effect observation.</summary>
[TestClass]
public sealed class StorageTransferPresentationTests
{
	/// <summary>Nested selectors retain their configured root and physical record order across projection.</summary>
	[TestMethod]
	public void SerializesNestedRecordsWithoutSessionReferences()
	{
		var root = new StorableReference(new("windows"), "container", new("file", @"C:\outer.zip"));
		var destination = new StorableReference(new("windows"), "destination", new("file", @"C:\output"));
		var archive = new ArchiveLocator(root).Append("7", "first.7z").Append("8", "second.zip");
		var source = new ArchiveRecordLocator(archive, "3", "same.txt");
		var values = StorageTransferDataMapper.ToData((StorageOperationRequest[])[new CopyOperationRequest(source, destination)]);
		Assert.AreEqual(1, values.Items.Length);
		Assert.AreEqual("windows", values.Items[0].Root.SourceId);
		Assert.AreEqual("container", values.Items[0].Root.ItemId);
		Assert.AreEqual(BackgroundServer.StorageTransferSourceKind.ArchiveRecord, values.Items[0].SourceKind);
		Assert.AreEqual("3", values.Items[0].RecordId);
		Assert.AreEqual(2, values.Items[0].ArchiveStepCount);
		CollectionAssert.AreEqual(new[] { "7", "8" }, values.Steps.Select(step => step.RecordId).ToArray());
	}

	/// <summary>A failed job preserves its committed destination independently of the success-only result item.</summary>
	[TestMethod]
	public void ProjectsPartialEffectsIntoTheCoreMonitorState()
	{
		var summary = new BackgroundServer.OperationSummary
		{
			Id = Guid.NewGuid(), Kind = "Copy", State = BackgroundServer.OperationState.Failed, HasItemProgress = true, CompletedItems = 1, TotalItems = 2,
			AcceptedAt = DateTimeOffset.UtcNow, ErrorMessage = "The next source failed.", HasTransferOutcome = true, TransferPublication = (int)StorageTransferPublication.Partial,
			TransferPhase = (int)StorageTransferPhase.Reading, TransferCommittedItems = 1, TransferCompletedBytes = 10,
			TransferDestination = new BackgroundServer.StorageReferenceData { SourceId = "windows", ItemId = "committed", HasAddress = true, AddressScheme = "file", AddressValue = @"C:\output\file.txt" },
		};
		var state = StorageOperationStatusMapper.ToState(StorageOperationStatusMapper.Convert(summary));
		Assert.AreEqual(StorageJobState.Failed, state.State);
		Assert.AreEqual(StorageTransferPublication.Partial, state.Transfer!.Publication);
		Assert.AreEqual("committed", state.Transfer.Destination!.ItemId);
		Assert.AreEqual(1, state.Transfer.CommittedItems);
		Assert.AreEqual(10L, state.Transfer.CompletedBytes);
	}
}

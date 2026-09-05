// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Windows;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for windows identity behavior.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowsIdentityTests
{
	/// <summary>
	/// Test case: file identity survives cold same directory rename and rejects replacement.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task FileIdentitySurvivesColdSameDirectoryRenameAndRejectsReplacement()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.IdentityTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		var oldPath = Path.Combine(directoryPath, "old.txt");
		var newPath = Path.Combine(directoryPath, "new.txt");
		File.WriteAllText(oldPath, "original");

		try
		{
			StorableReference originalReference;
			string originalId;

			await using (var originalSource = new WindowsStorageSource())
			{
				var original = (IWindowsStorable)await originalSource.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, oldPath));
				originalId = original.Id;
				originalReference = new StorableReference(originalSource.SourceId, original.Id, original.Address);

				StringAssert.StartsWith(original.Id, "winfs:v1:");
				Assert.AreEqual(WindowsStorageSource.FileAddressScheme, original.Address.Scheme);
				Assert.AreEqual(oldPath, original.Address.Value);
			}

			File.Move(oldPath, newPath);

			await using var restoredSource = new WindowsStorageSource();
			var renamed = (IWindowsStorable)await restoredSource.ResolveAsync(originalReference);
			Assert.AreEqual(originalId, renamed.Id);
			Assert.AreEqual(WindowsStorageSource.FileAddressScheme, renamed.Address.Scheme);
			Assert.AreEqual(newPath, renamed.Address.Value);
			Assert.AreEqual(newPath, renamed.FileSystemPath);

			File.Delete(newPath);
			File.WriteAllText(newPath, "recreated");

			var recreated = (IWindowsStorable)await restoredSource.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, newPath));
			Assert.AreNotEqual(renamed.Id, recreated.Id);
			await Assert.ThrowsAsync<FileNotFoundException>(async () => await restoredSource.ResolveAsync(originalReference));
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}
}

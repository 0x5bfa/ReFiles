// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Windows;

namespace Files.UnitTests;

/// <summary>Verifies Windows Shell default-command discovery.</summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowsShellDefaultCommandTests
{
	/// <summary>Verifies that a file-system folder exposes a navigation command.</summary>
	[TestMethod]
	public async Task FileSystemFolderExposesNavigationDefaultCommand()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.DefaultCommandTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var folder = (IWindowsStorable)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, directoryPath));
			var reference = new StorableReference(source.SourceId, folder.Id, folder.Address);
			var invoker = new WindowsShellDefaultCommandInvoker(source);

			var command = await invoker.GetDefaultCommandAsync(reference);

			Assert.IsNotNull(command);
			Assert.IsTrue(
				command.CanonicalVerb is not null
				&& (command.CanonicalVerb.Equals("open", StringComparison.OrdinalIgnoreCase) || command.CanonicalVerb.Equals("explore", StringComparison.OrdinalIgnoreCase)));
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}
}

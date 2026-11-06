// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0
using Files.Hosting;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Archives;
using Files.Core.Storage;
using System.IO.Compression;

var path = Path.Combine(Path.GetTempPath(), $"ReFiles.PackageSmoke.{Guid.NewGuid():N}.zip");
try
{
	using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
	{
		using var writer = new StreamWriter(zip.CreateEntry("content.txt").Open());
		writer.Write("package smoke test");
	}

	await using var runtime = FilesHost.CreateUIRuntime();
	await using var item = await runtime.Workspace.ResolveAsync(new StorageAddress("file", path));
	var capability = item.Get<IArchiveSource>() ?? throw new InvalidOperationException("Archive capability missing.");
	await using var archive = await capability.OpenAsync();
	if (archive.Entries.Count != 1)
	{
		throw new InvalidOperationException("Native archive reader returned the wrong entries.");
	}

	await using var pane = runtime.CreateBrowsePane();
	await using var prefetch = ((Files.Core.Sessions.IBrowsePaneSession)pane.Content).BrowseSession.CreatePrefetchCoordinator();
	if (runtime.Windows.PropertyActions is null || runtime.Windows.ItemPropertiesFileSystem is null)
	{
		throw new InvalidOperationException("Windows property services missing.");
	}
}
finally
{
	File.Delete(path);
}

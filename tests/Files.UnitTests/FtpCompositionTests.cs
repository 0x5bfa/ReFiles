// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;
using Files.Core.Capabilities.Previews;
using Files.Core.Capabilities.Properties;
using Files.Core.Composition;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Ftp;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for ftp composition behavior.
/// </summary>
[TestClass]
public sealed class FtpCompositionTests
{
	/// <summary>
	/// Test case: builder adds ftp properties previews and operations.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task BuilderAddsFtpPropertiesPreviewsAndOperations()
	{
		var sessions = new InMemoryFtpSessionFactory();
		sessions.AddFile("/notes.txt", new byte[37], new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero));
		var profile = new FtpConnectionProfile("composition", "Composition FTP", "example.test");
		var runtime = new FilesCoreBuilder()
			.AddFtpStorage(profile, sessionFactory: sessions, enableArchives: false)
			.Build();

		try
		{
			var source = (FtpStorageSource)runtime
				.Workspace
				.Sources
				.Single();
			await using var model = await runtime.Workspace.ResolveAsync(source.SourceId, source.CreateAddress(FtpPath.Parse("/notes.txt")));
			var properties = model.Get<IPropertySource>();
			var preview = model.Get<IPreviewSource>();
			Assert.IsNotNull(properties);
			Assert.IsNotNull(preview);

			var values = await properties.GetPropertiesAsync(new PropertyRequest(["System.Size", "System.DateModified"]));
			Assert.AreEqual((ulong)37, (ulong)values["System.Size"]!);
			Assert.IsTrue(values.ContainsKey("System.DateModified"));

			Assert.IsTrue(runtime.StorageOperations.CanHandle(new RenameOperationRequest(source.CreateReference(FtpPath.Parse("/notes.txt")), "renamed.txt")));
		}
		finally
		{
			await runtime.DisposeAsync();
		}
	}
}

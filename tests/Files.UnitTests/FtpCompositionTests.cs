// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;
using Files.Core.Capabilities.Content;
using Files.Core.Capabilities.Properties;
using Files.Core;
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
	/// <summary>Operation hosts can resolve content for FTP sources registered after their runtime is built.</summary>
	/// <returns>The asynchronous regression test.</returns>
	[TestMethod]
	public async Task TransferRuntimeSupportsSourcesRegisteredAfterBuild()
	{
		var sessions = new InMemoryFtpSessionFactory();
		sessions.AddFile("/notes.txt", [1, 2, 3]);
		await using var runtime = new FilesCoreBuilder().AddStorageTransfers().Build();
		var source = new FtpStorageSource(new("dynamic", "Dynamic", "example.test"), sessionFactory: sessions);
		runtime.Workspace.SourceRegistry!.AddSource(source);
		await using var model = await runtime.Workspace.ResolveAsync(source.CreateReference(FtpPath.Parse("/notes.txt")));
		var reader = model.Get<IReadableContent>();
		Assert.IsNotNull(reader);
		Assert.IsNotNull(model.Get<IWritableContent>());
		await using var stream = await reader.OpenReadAsync();
		using var output = new MemoryStream();
		await stream.CopyToAsync(output);
		CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, output.ToArray());
	}

	/// <summary>Multiple FTP sources share content registrations without exposing unregistered or unrelated models.</summary>
	/// <returns>The asynchronous regression test.</returns>
	[TestMethod]
	public async Task MultipleSourcesUseExplicitContentRegistrations()
	{
		var sessions = new InMemoryFtpSessionFactory();
		sessions.AddFile("/notes.txt", [1, 2, 3]);
		await using var runtime = new FilesCoreBuilder()
			.AddFtpStorage(new("first", "First", "example.test"), sessionFactory: sessions, enableArchives: false)
			.AddFtpStorage(new("second", "Second", "example.test"), sessionFactory: sessions, enableArchives: false).AddStorageTransfers().Build();
		var sources = runtime.Workspace.Sources.OfType<FtpStorageSource>().ToArray();
		Assert.HasCount(2, sources);

		foreach (var source in sources)
		{
			var reference = source.CreateReference(FtpPath.Parse("/notes.txt"));
			await using var model = await runtime.Workspace.ResolveAsync(reference);
			var reader = model.Get<IReadableContent>();
			Assert.IsNotNull(reader);
			Assert.IsNotNull(model.Get<IWritableContent>());
			await using var stream = await reader.OpenReadAsync();
			using var output = new MemoryStream();
			await stream.CopyToAsync(output);
			CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, output.ToArray());

			await using var rawModel = new StorableModelFactory().Create(source, await source.ResolveAsync(reference));
			Assert.IsNull(rawModel.Get<IReadableContent>());
			Assert.IsNull(rawModel.Get<IWritableContent>());
		}
	}

	/// <summary>Client-side FTP storage uses the explicitly selected execution boundary in either registration order.</summary>
	/// <param name="externalFirst">Whether the external service is configured first.</param>
	/// <returns>The asynchronous regression test.</returns>
	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public async Task ClientSourcesDoNotRegisterLocalOperations(bool externalFirst)
	{
		var sessions = new InMemoryFtpSessionFactory();
		sessions.AddFile("/notes.txt", [1]);
		var profile = new FtpConnectionProfile("client", "Client", "example.test");
		var external = new StorageOperationService([]);
		await using var builder = new FilesCoreBuilder();
		if (externalFirst)
		{
			builder.UseStorageOperations(external);
		}

		builder.AddFtpStorage(profile, sessionFactory: sessions, enableArchives: false, enableOperations: false);
		if (!externalFirst)
		{
			builder.UseStorageOperations(external);
		}

		await using var runtime = builder.Build();
		var source = (FtpStorageSource)runtime.Workspace.Sources.Single();
		await using var model = await runtime.Workspace.ResolveAsync(source.CreateReference(FtpPath.Parse("/notes.txt")));

		Assert.AreEqual("notes.txt", model.Name);
		Assert.AreSame(external, runtime.StorageOperations);
		Assert.IsFalse(runtime.StorageOperations.CanHandle(new DeleteOperationRequest(model.Reference)));
	}

	/// <summary>
	/// Test case: builder adds ftp properties and operations.
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
			Assert.IsNotNull(properties);
			Assert.IsNull(model.Get<Files.Core.Capabilities.Previews.IPreviewSource>());

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

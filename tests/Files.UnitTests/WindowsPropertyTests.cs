// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Composition;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Properties;
using Files.Core.Models;
using Files.Core.Sessions;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
using Files.Core.ViewSettings;
using OwlCore.Storage;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for windows property behavior.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowsPropertyTests
{
	/// <summary>
	/// Test case: windows property reader reads only requested properties.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task WindowsPropertyReaderReadsOnlyRequestedProperties()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.PropertyTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		var filePath = Path.Combine(directoryPath, "properties.bin");
		var content = new byte[37];
		File.WriteAllBytes(filePath, content);

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var coreModel = await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, filePath));

			var capabilityRegistry = new CapabilityBuilder()
				.Add<IPropertySource>(new PropertySourceFactory(new WindowsPropertyReader()), origin: "Windows Property System")
				.SetCombiner<IPropertySource>(new PropertySourceCombiner())
				.Build();

			using var model = new StorableModelFactory(capabilityRegistry).Create(source, coreModel);
			var propertySource = model.Get<IPropertySource>();
			Assert.IsNotNull(propertySource);

			var properties = await propertySource.GetPropertiesAsync(new PropertyRequest(["System.Size"]));

			Assert.AreEqual((ulong)content.Length, (ulong)properties["System.Size"]!);
			Assert.AreEqual(1, properties.Count);
			Assert.IsFalse(properties.ContainsKey("System.DateModified"));
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	/// <summary>
	/// Test case: windows property reader includes Shell-formatted display text when requested.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task WindowsPropertyReaderIncludesShellFormattedDisplayTextWhenRequested()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.PropertyTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		var filePath = Path.Combine(directoryPath, "properties.bin");
		var content = new byte[1_463_984];
		File.WriteAllBytes(filePath, content);

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var coreModel = await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, filePath));

			var capabilityRegistry = new CapabilityBuilder()
				.Add<IPropertySource>(new PropertySourceFactory(new WindowsPropertyReader()), origin: "Windows Property System")
				.SetCombiner<IPropertySource>(new PropertySourceCombiner())
				.Build();

			using var model = new StorableModelFactory(capabilityRegistry).Create(source, coreModel);
			var propertySource = model.Get<IPropertySource>();
			Assert.IsNotNull(propertySource);

			var properties = await propertySource.GetPropertiesAsync(new PropertyRequest(["System.Size"], includeFormattedValues: true));
			var size = Assert.IsInstanceOfType<FormattedPropertyValue>(properties["System.Size"]);

			Assert.AreEqual((ulong)content.Length, size.RawValue);
			Assert.IsFalse(string.IsNullOrWhiteSpace(size.DisplayText));
			Assert.AreNotEqual(content.Length.ToString(), size.DisplayText);
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	/// <summary>
	/// Test case: windows property reader preserves the stored Shell PIDL when reading formatted values.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task WindowsPropertyReaderUsesStoredPidlForFormattedValues()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.PropertyTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		var filePath = Path.Combine(directoryPath, "properties.bin");
		File.WriteAllBytes(filePath, new byte[37]);

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var resolved = Assert.IsInstanceOfType<WindowsStorable>(await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, filePath)));
			var descriptor = resolved.Descriptor with { Locator = new WindowsItemLocator(resolved.Locator.AbsolutePidl, @"Z:\missing\properties.bin") };
			var coreModel = resolved.Factory.Create(descriptor);
			var capabilityRegistry = new CapabilityBuilder()
				.Add<IPropertySource>(new PropertySourceFactory(new WindowsPropertyReader()), origin: "Windows Property System")
				.SetCombiner<IPropertySource>(new PropertySourceCombiner())
				.Build();

			using var model = new StorableModelFactory(capabilityRegistry).Create(source, coreModel);
			var propertySource = model.Get<IPropertySource>();
			Assert.IsNotNull(propertySource);

			var properties = await propertySource.GetPropertiesAsync(new PropertyRequest(["System.Size"], includeFormattedValues: true));
			var size = Assert.IsInstanceOfType<FormattedPropertyValue>(properties["System.Size"]);

			Assert.AreEqual((ulong)37, size.RawValue);
			Assert.IsFalse(string.IsNullOrWhiteSpace(size.DisplayText));
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	/// <summary>
	/// Test case: windows property reader reads shell details property.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task WindowsPropertyReaderReadsShellDetailsProperty()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.PropertyTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		var filePath = Path.Combine(directoryPath, "properties.bin");
		File.WriteAllBytes(filePath, new byte[1]);

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var coreModel = await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, filePath));

			var capabilityRegistry = new CapabilityBuilder()
				.Add<IPropertySource>(new PropertySourceFactory(new WindowsPropertyReader()), origin: "Windows Property System")
				.SetCombiner<IPropertySource>(new PropertySourceCombiner())
				.Build();

			using var model = new StorableModelFactory(capabilityRegistry).Create(source, coreModel);
			var propertySource = model.Get<IPropertySource>();
			Assert.IsNotNull(propertySource);

			var properties = await propertySource.GetPropertiesAsync(new PropertyRequest(["System.ItemNameDisplay"]));

			Assert.AreEqual("properties.bin", properties["System.ItemNameDisplay"]);
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	/// <summary>
	/// Test case: browse prefetch loads properties for every visible windows item.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task BrowsePrefetchLoadsPropertiesForEveryVisibleWindowsItem()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.PropertyTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		for (var index = 0; index < 32; index++)
		{
			File.WriteAllBytes(Path.Combine(directoryPath, $"item-{index:D2}.bin"), new byte[index + 1]);
		}

		try
		{
			await using var runtime = new FilesCoreBuilder().AddWindowsStorage(enablePreviews: false, enableArchives: false).Build();
			var folderModel = await runtime.Workspace.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, directoryPath));
			var reference = folderModel.Reference;
			await folderModel.DisposeAsync();
			var window = await runtime.ShellSession.CreateWindowAsync(new FolderLocation(reference));
			var pane = window.ActiveTab?.ActivePane?.Content as BrowsePaneSession;
			Assert.IsNotNull(pane);
			var session = pane.BrowseSession;
			Assert.AreEqual(32, session.Items.Count);
			Assert.IsTrue(session.Items.All(static item => item.GetCoreModel() is WindowsStorable windowsItem && windowsItem.Locator.ParentFolder is not null && !windowsItem.Locator.RelativePidl.IsEmpty));
			ViewColumnSettings[] columns =
			[
				new ViewColumnSettings("System.Size", 120, 0),
				new ViewColumnSettings("System.ItemTypeText", 120, 1),
				new ViewColumnSettings("System.DateModified", 120, 2),
			];
			var settings = new BrowseViewSettings(layoutMode: ViewLayoutMode.Details, columns: columns);
			await session.UpdateViewSettingsAsync(settings);
			await using var coordinator = new BrowsePrefetchCoordinator(session);

			coordinator.UpdateViewport(new BrowseViewport(0, session.Items.Count, lookAheadCount: 0), settings, session.Generation);

			await WaitUntilAsync(() => session.Items.All(item => HasRequestedProperties(session, item)));
			Assert.IsTrue(session.Items.All(item => HasRequestedProperties(session, item)));
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}

	private static bool HasRequestedProperties(IBrowseSession session, IStorableModel item)
	{
		return session.TryGetPresentation(item.Reference.GetKey(), out var presentation)
			&& presentation.Properties.ContainsKey("System.Size")
			&& presentation.Properties.ContainsKey("System.ItemTypeText")
			&& presentation.Properties.ContainsKey("System.DateModified");
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (!condition() && DateTime.UtcNow < timeout)
		{
			await Task.Delay(20);
		}
	}
}

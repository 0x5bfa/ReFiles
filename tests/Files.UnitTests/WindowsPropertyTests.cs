// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;
using Files.Core.ItemFeatures.Properties;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
using OwlCore.Storage;

namespace Files.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class WindowsPropertyTests
{
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

			var featureRegistry = new ItemFeatureBuilder()
				.Add<IPropertySource>(new PropertySourceFactory(new WindowsPropertyReader()), origin: "Windows Property System")
				.SetCombiner<IPropertySource>(new PropertySourceCombiner())
				.Build();

			using var model = new StorableModelFactory(featureRegistry).Create(source, coreModel);
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

			var featureRegistry = new ItemFeatureBuilder()
				.Add<IPropertySource>(new PropertySourceFactory(new WindowsPropertyReader()), origin: "Windows Property System")
				.SetCombiner<IPropertySource>(new PropertySourceCombiner())
				.Build();

			using var model = new StorableModelFactory(featureRegistry).Create(source, coreModel);
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
}

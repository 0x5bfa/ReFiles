// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.Storage;
using Files.ItemProperties;
using Files.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.UITests;

/// <summary>
/// Verifies the Shell-style item properties model.
/// </summary>
[TestClass]
public sealed class ItemPropertiesViewModelTests
{
	/// <summary>
	/// Verifies that details retain common values and identify mixed values.
	/// </summary>
	[TestMethod]
	public void DetailsRetainCommonValuesAndIdentifyMixedValues()
	{
		var first = CreateItem("first.txt", false, null, ("System.Author", "Files"), ("System.Rating", 1));
		var second = CreateItem("second.txt", false, null, ("System.Author", "Files"), ("System.Rating", 2));

		var viewModel = new ItemPropertiesViewModel([first, second]);

		Assert.AreEqual("Files", viewModel.Details.Single(detail => detail.Name == "Author").Value);
		Assert.AreNotEqual("1", viewModel.Details.Single(detail => detail.Name == "Rating").Value);
	}

	/// <summary>
	/// Verifies that folder contents are counted and sized asynchronously.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[TestMethod]
	public async Task InitializeReadsFolderContentsAsync()
	{
		var directory = CreateTemporaryDirectory();
		try
		{
			File.WriteAllBytes(Path.Combine(directory, "one.bin"), new byte[1_024]);
			var child = Directory.CreateDirectory(Path.Combine(directory, "child")).FullName;
			File.WriteAllBytes(Path.Combine(child, "two.bin"), new byte[2_048]);
			var viewModel = new ItemPropertiesViewModel([CreateItem(Path.GetFileName(directory), true, directory)]);

			await viewModel.InitializeAsync();

			StringAssert.Contains(viewModel.Contains, "2");
			StringAssert.Contains(viewModel.Size, "3.0");
		}
		finally
		{
			DeleteTemporaryDirectory(directory);
		}
	}

	/// <summary>
	/// Verifies that applying an attribute change updates a filesystem item.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[TestMethod]
	public async Task ApplyUpdatesFileAttributesAsync()
	{
		var directory = CreateTemporaryDirectory();
		var path = Path.Combine(directory, "item.txt");
		try
		{
			File.WriteAllText(path, "content");
			var viewModel = new ItemPropertiesViewModel([CreateItem(Path.GetFileName(path), false, path)]);
			viewModel.IsReadOnly = true;

			await viewModel.ApplyAsync();

			Assert.IsTrue(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
			Assert.IsFalse(viewModel.HasChanges);
		}
		finally
		{
			if (File.Exists(path))
			{
				File.SetAttributes(path, FileAttributes.Normal);
			}

			DeleteTemporaryDirectory(directory);
		}
	}

	/// <summary>
	/// Verifies that the General page type includes the selected file's extension.
	/// </summary>
	[TestMethod]
	public void GeneralTypeIncludesFileExtension()
	{
		var viewModel = new ItemPropertiesViewModel([CreateItem("item.txt", false, null, ("System.ItemTypeText", "Text Document"))]);

		Assert.AreEqual("Text Document (.txt)", viewModel.Type);
	}

	/// <summary>
	/// Verifies that renaming from a display name preserves an extension hidden by the browse view.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[TestMethod]
	public async Task ApplyRenamePreservesHiddenExtensionAsync()
	{
		var directory = CreateTemporaryDirectory();
		var path = Path.Combine(directory, "before.txt");
		var renamedPath = Path.Combine(directory, "after.txt");
		try
		{
			File.WriteAllText(path, "content");
			var reference = new StorableReference(new StorageSourceId("test"), path, new StorageAddress("file", path));
			var item = new BrowseItemViewModel(Path.GetFileName(path), false, reference, showFileExtensions: false);
			var viewModel = new ItemPropertiesViewModel([item]) { Name = "after" };

			await viewModel.ApplyAsync();

			Assert.IsFalse(File.Exists(path));
			Assert.IsTrue(File.Exists(renamedPath));
		}
		finally
		{
			DeleteTemporaryDirectory(directory);
		}
	}

	private static BrowseItemViewModel CreateItem(string name, bool isFolder, string? path, params (string PropertyId, object? Value)[] properties)
	{
		var address = path is null ? new StorageAddress("test", name) : new StorageAddress("file", path);
		var reference = new StorableReference(new StorageSourceId("test"), name, address);
		var item = new BrowseItemViewModel(name, isFolder, reference);
		if (properties.Length is not 0)
		{
			item.SetProperties(properties.ToDictionary(static property => property.PropertyId, static property => property.Value, StringComparer.Ordinal));
		}

		return item;
	}

	private static string CreateTemporaryDirectory()
	{
		var path = Path.Combine(Path.GetTempPath(), $"ReFiles.ItemProperties.{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);

		return path;
	}

	private static void DeleteTemporaryDirectory(string path)
	{
		if (Directory.Exists(path))
		{
			Directory.Delete(path, true);
		}
	}
}

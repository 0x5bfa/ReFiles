// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.ComponentModel;
using System.IO;
using Files.Hosting;

namespace Files.UnitTests;

/// <summary>Verifies atomic settings persistence independently of Shell and WinUI startup.</summary>
[TestClass]
public sealed class WindowsSettingsStorageTests
{
	/// <summary>Creates and replaces complete UTF-8 documents without leaving temporary files.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task CreatesAndReplacesSettingsBeforeRuntimeConstruction()
	{
		var root = Path.Combine(Path.GetTempPath(), $"Files.SettingsStorageTests-{Guid.NewGuid():N}");
		try
		{
			var storage = FilesHost.OpenSettingsStorage(root);
			Assert.IsNull(await storage.ReadTextAsync("settings.json"));
			await storage.WriteTextAsync("settings.json", "{\"language\":\"日本語\"}");
			await storage.WriteTextAsync("settings.json", "{\"language\":\"English\"}");
			Assert.AreEqual("{\"language\":\"English\"}", await storage.ReadTextAsync("settings.json"));
			var bytes = await File.ReadAllBytesAsync(Path.Combine(root, "Settings", "settings.json"));
			Assert.IsFalse(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
			Assert.HasCount(1, Directory.GetFiles(Path.Combine(root, "Settings")));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	/// <summary>A failed atomic replacement retains the previous document and cleans up the temporary file.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task FailedReplacementRetainsTheOriginalDocument()
	{
		var root = Path.Combine(Path.GetTempPath(), $"Files.SettingsStorageTests-{Guid.NewGuid():N}");
		try
		{
			var storage = FilesHost.OpenSettingsStorage(root);
			await storage.WriteTextAsync("settings.json", "original");
			using (var lease = new FileStream(Path.Combine(root, "Settings", "settings.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				await Assert.ThrowsAsync<Win32Exception>(() => storage.WriteTextAsync("settings.json", "replacement"));
				Assert.AreEqual("original", await storage.ReadTextAsync("settings.json"));
				Assert.HasCount(1, Directory.GetFiles(Path.Combine(root, "Settings")));
			}

			await storage.WriteTextAsync("settings.json", "retry");
			Assert.AreEqual("retry", await storage.ReadTextAsync("settings.json"));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	/// <summary>Rejects path components before any settings directory or file is created.</summary>
	/// <param name="fileName">The invalid document filename.</param>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	[DataRow("../escape.json")]
	[DataRow("..\\escape.json")]
	[DataRow("settings.json:stream")]
	public async Task RejectsPathsOutsideTheSettingsDirectory(string fileName)
	{
		var root = Path.Combine(Path.GetTempPath(), $"Files.SettingsStorageTests-{Guid.NewGuid():N}");
		var storage = FilesHost.OpenSettingsStorage(root);
		await Assert.ThrowsAsync<ArgumentException>(() => storage.ReadTextAsync(fileName));
		await Assert.ThrowsAsync<ArgumentException>(() => storage.WriteTextAsync(fileName, "value"));
		Assert.IsFalse(Directory.Exists(root));
	}

	/// <summary>Cancellation before commit retains the previous document.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task CancelledWriteRetainsTheOriginalDocument()
	{
		var root = Path.Combine(Path.GetTempPath(), $"Files.SettingsStorageTests-{Guid.NewGuid():N}");
		try
		{
			var storage = FilesHost.OpenSettingsStorage(root);
			await storage.WriteTextAsync("view-state.json", "original");
			using var cancellation = new CancellationTokenSource();
			cancellation.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(() => storage.WriteTextAsync("view-state.json", "replacement", cancellation.Token));
			Assert.AreEqual("original", await storage.ReadTextAsync("view-state.json"));
			Assert.HasCount(1, Directory.GetFiles(Path.Combine(root, "Settings")));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}
}

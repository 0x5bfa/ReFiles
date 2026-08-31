// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Windows;
using Windows.Win32.Foundation;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for Windows Shell location behavior.
/// </summary>
[TestClass]
public sealed class WindowsShellLocationTests
{
	/// <summary>
	/// Test case: WSL namespace and UNC parsing names are recognized.
	/// </summary>
	/// <param name="parsingName">The parsing name to classify.</param>
	[TestMethod]
	[DataRow(@"\\wsl.localhost")]
	[DataRow(@"\\wsl.localhost\Ubuntu\usr\bin")]
	[DataRow(@"\\wsl$")]
	[DataRow(@"\\wsl$\Ubuntu")]
	[DataRow("shell:::{B2B4A4D1-2754-4140-A2EB-9A76D9D7CDC6}")]
	[DataRow("::{b2b4a4d1-2754-4140-a2eb-9a76d9d7cdc6}")]
	public void WslParsingNamesAreRecognized(string parsingName)
	{
		Assert.IsTrue(WindowsShellLocation.IsWsl(parsingName));
	}

	/// <summary>
	/// Test case: similarly named local and UNC locations are not classified as WSL.
	/// </summary>
	/// <param name="parsingName">The parsing name to classify.</param>
	[TestMethod]
	[DataRow(@"C:\wsl.localhost\Ubuntu")]
	[DataRow(@"\\wsl.localhost.example\Ubuntu")]
	[DataRow(@"\\server\wsl$\Ubuntu")]
	[DataRow(@"C:\items\{B2B4A4D1-2754-4140-A2EB-9A76D9D7CDC6}\document.txt")]
	public void NonWslParsingNamesAreNotRecognized(string parsingName)
	{
		Assert.IsFalse(WindowsShellLocation.IsWsl(parsingName));
	}

	/// <summary>
	/// Test case: WSL enumeration suppresses owner UI while ordinary folders retain it.
	/// </summary>
	[TestMethod]
	public void WslEnumerationSuppressesOwnerWindow()
	{
		var requestedOwnerWindow = new HWND(1234);

		Assert.IsTrue(WindowsStorableFactory.GetEnumerationOwnerWindow(@"\\wsl.localhost\Ubuntu", requestedOwnerWindow).IsNull);
		Assert.AreEqual(requestedOwnerWindow, WindowsStorableFactory.GetEnumerationOwnerWindow(@"C:\Windows", requestedOwnerWindow));
	}

	/// <summary>
	/// Test case: WSL items use parsing-name identity without remote file metadata reads.
	/// </summary>
	[TestMethod]
	public void WslItemsUseParsingNameIdentity()
	{
		const string parsingName = @"\\wsl.localhost\Ubuntu\usr\bin\bash";
		var reader = new WindowsItemIdReader();
		var itemId = reader.GetItemId(parsingName, parsingName);

		Assert.IsFalse(reader.IsFileSystemIdentity(itemId));
		Assert.IsTrue(reader.TryGetParsingName(itemId, out var restoredParsingName));
		Assert.AreEqual(parsingName, restoredParsingName);
	}
}

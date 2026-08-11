// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Storage.Ftp;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for ftp path behavior.
/// </summary>
[TestClass]
public sealed class FtpPathTests
{
	/// <summary>
	/// Test case: normalizes separators and dot segments without changing case.
	/// </summary>
	[TestMethod]
	public void NormalizesSeparatorsAndDotSegmentsWithoutChangingCase()
	{
		var path = FtpPath.Parse(@"Home\Documents\.\Drafts\..\Report.txt");

		Assert.AreEqual("/Home/Documents/Report.txt", path.Value);
		Assert.AreEqual("Report.txt", path.Name);
		Assert.AreEqual("/Home/Documents", path.Parent!.Value);
		Assert.AreEqual("/Home/Documents/Report.txt", FtpPath.ParseEscapedUriPath(path.ToEscapedUriPath()).Value);
	}

	/// <summary>
	/// Test case: rejects paths that escape the remote root.
	/// </summary>
	[TestMethod]
	public void RejectsPathsThatEscapeTheRemoteRoot()
	{
		Assert.Throws<ArgumentException>(() => FtpPath.Parse("../../outside"));
		Assert.Throws<ArgumentException>(() => FtpPath.Root.Combine("../outside"));
	}

	/// <summary>
	/// Test case: root containment honors the configured comparison.
	/// </summary>
	[TestMethod]
	public void RootContainmentHonorsTheConfiguredComparison()
	{
		var root = FtpPath.Parse("/Home");
		var candidate = FtpPath.Parse("/home/Documents");

		Assert.IsFalse(candidate.IsWithin(root, StringComparer.Ordinal));
		Assert.IsTrue(candidate.IsWithin(root, StringComparer.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Test case: source rejects credentials and escaped separators in addresses.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task SourceRejectsCredentialsAndEscapedSeparatorsInAddresses()
	{
		var profile = new FtpConnectionProfile("address-security", "Address security FTP", "example.test", rootPath: "/home");
		await using var source = new FtpStorageSource(profile);

		Assert.IsFalse(
			source.CanResolve(
				new StorageAddress(
					"ftp",
					"//user:password@example.test:21/home/item")));
		Assert.IsFalse(
			source.CanResolve(
				new StorageAddress(
					"ftp",
					"//example.test:21/home/%2Foutside")));
		Assert.IsFalse(
			source.CanResolve(
				new StorageAddress(
					"ftp",
					"//example.test:21/outside")));
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Storage.Ftp;

namespace Files.UnitTests;

[TestClass]
public sealed class FtpPathTests
{
	[TestMethod]
	public void NormalizesSeparatorsAndDotSegmentsWithoutChangingCase()
	{
		var path = FtpPath.Parse(@"Home\Documents\.\Drafts\..\Report.txt");

		Assert.AreEqual("/Home/Documents/Report.txt", path.Value);
		Assert.AreEqual("Report.txt", path.Name);
		Assert.AreEqual("/Home/Documents", path.Parent!.Value);
		Assert.AreEqual("/Home/Documents/Report.txt", FtpPath.ParseEscapedUriPath(path.ToEscapedUriPath()).Value);
	}

	[TestMethod]
	public void RejectsPathsThatEscapeTheRemoteRoot()
	{
		Assert.Throws<ArgumentException>(() => FtpPath.Parse("../../outside"));
		Assert.Throws<ArgumentException>(() => FtpPath.Root.Combine("../outside"));
	}

	[TestMethod]
	public void RootContainmentHonorsTheConfiguredComparison()
	{
		var root = FtpPath.Parse("/Home");
		var candidate = FtpPath.Parse("/home/Documents");

		Assert.IsFalse(candidate.IsWithin(root, StringComparer.Ordinal));
		Assert.IsTrue(candidate.IsWithin(root, StringComparer.OrdinalIgnoreCase));
	}

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

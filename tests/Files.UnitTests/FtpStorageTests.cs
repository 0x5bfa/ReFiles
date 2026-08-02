// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Text;
using Files.Core.Storage;
using Files.Core.Storage.Ftp;

namespace Files.UnitTests;

[TestClass]
public sealed class FtpStorageTests
{
	[TestMethod]
	public async Task ResolvesAddressesAndEnumeratesOneConfiguredRoot()
	{
		var sessions = new InMemoryFtpSessionFactory();
		sessions.AddFolder("/home/Documents");
		sessions.AddFile("/home/readme.txt", Encoding.UTF8.GetBytes("hello"));
		var profile = new FtpConnectionProfile("primary", "Primary FTP", "example.test", rootPath: "/home", userNameHint: "files-user");
		await using var source = new FtpStorageSource(profile, new StaticFtpCredentialResolver(new FtpCredential("files-user", "secret")), sessions);

		FtpFolder? root = null;
		await foreach (var candidate in source.GetRootsAsync())
		{
			root = (FtpFolder)candidate;
		}

		Assert.IsNotNull(root);
		Assert.AreEqual("/home", root.Id);
		var items = new List<IStorableChild>();
		await foreach (var item in root.GetItemsAsync())
		{
			items.Add(item);
		}

		Assert.AreEqual(2, items.Count);
		var file = (FtpFile)items.Single(item => item.Name == "readme.txt");
		Assert.IsTrue(source.CanResolve(file.Address));
		Assert.IsFalse(file.Address.ToString().Contains("secret", StringComparison.Ordinal));
		Assert.AreEqual(
			"ftp://example.test:21/home/readme.txt",
			file.Address.ToString());

		var resolved = await source.ResolveAsync(file.Address);
		Assert.AreEqual(file.Id, resolved.Id);
		Assert.AreEqual("files-user", sessions.LastCredential!.UserName);
	}

	[TestMethod]
	public async Task ReturnedStreamOwnsSessionUntilStreamDisposal()
	{
		var sessions = new InMemoryFtpSessionFactory();
		sessions.AddFile("/payload.bin", [1, 2, 3, 4]);
		var profile = new FtpConnectionProfile("stream", "Stream FTP", "example.test");
		await using var source = new FtpStorageSource(profile, sessionFactory: sessions);
		var file = (FtpFile)await source.ResolveAsync(source.CreateAddress(FtpPath.Parse("/payload.bin")));

		var stream = await file.OpenStreamAsync(FileAccess.Read);
		var session = sessions.Sessions[^1];
		Assert.IsFalse(session.IsDisposed);

		var content = new byte[4];
		var bytesRead = await stream.ReadAsync(content);
		Assert.AreEqual(4, bytesRead);
		CollectionAssert.AreEqual(new byte[] {1, 2, 3, 4}, content);

		await stream.DisposeAsync();
		Assert.IsTrue(session.IsDisposed);
		Assert.AreEqual(1, session.CompletedTransferCount);
	}

	[TestMethod]
	public async Task StreamDisposalPropagatesFinalTransferFailure()
	{
		var sessions = new InMemoryFtpSessionFactory
		{
			CompleteTransferError =
				new IOException("Rejected test transfer."),
		};
		sessions.AddFile("/payload.bin", [1]);
		var profile = new FtpConnectionProfile("failed-transfer", "Failed transfer FTP", "example.test");
		await using var source = new FtpStorageSource(profile, sessionFactory: sessions);
		var file = (FtpFile)await source.ResolveAsync(source.CreateAddress(FtpPath.Parse("/payload.bin")));

		var stream = await file.OpenStreamAsync(FileAccess.Read);
		var session = sessions.Sessions[^1];

		await Assert.ThrowsAsync<IOException>(() => stream.DisposeAsync().AsTask());
		Assert.IsTrue(session.IsDisposed);
		Assert.AreEqual(1, session.CompletedTransferCount);
	}

	[TestMethod]
	public async Task ReferencesDoNotFollowAPathChangingRename()
	{
		var sessions = new InMemoryFtpSessionFactory();
		sessions.AddFile("/old.txt", [1]);
		var profile = new FtpConnectionProfile("identity", "Identity FTP", "example.test");
		await using var source = new FtpStorageSource(profile, sessionFactory: sessions);
		var original = source.CreateReference(FtpPath.Parse("/old.txt"));
		var handler = new FtpStorageOperationHandler(source);

		var result = await handler.ExecuteAsync(new RenameOperationRequest(original, "new.txt"));

		Assert.IsTrue(result.Succeeded, result.Error?.ToString());
		Assert.AreEqual("/new.txt", result.ResultItem!.ItemId);
		await Assert.ThrowsAsync<FileNotFoundException>(() => source .ResolveAsync(original) .AsTask());
	}

	[TestMethod]
	public async Task AuthenticationFailureRequestsOneRefreshedCredential()
	{
		var sessions = new InMemoryFtpSessionFactory();
		sessions.AddFile("/secured.txt", [1]);
		var credentials = new SequenceCredentialResolver(new FtpCredential("user", "wrong"), new FtpCredential("user", "correct"));
		var checkingFactory =
			new CredentialCheckingSessionFactory(sessions);
		var profile = new FtpConnectionProfile("authentication", "Authentication FTP", "example.test");
		await using var source = new FtpStorageSource(profile, credentials, checkingFactory);

		var resolved = await source.ResolveAsync(source.CreateAddress(FtpPath.Parse("/secured.txt")));

		Assert.AreEqual("secured.txt", resolved.Name);
		Assert.AreEqual(2, credentials.Requests.Count);
		Assert.IsFalse(credentials.Requests[0].IsRetry);
		Assert.IsTrue(credentials.Requests[1].IsRetry);
	}

	private sealed class SequenceCredentialResolver :
		IFtpCredentialResolver
	{
		private readonly Queue<FtpCredential> credentials;

		public SequenceCredentialResolver(params FtpCredential[] credentials)
		{
			this.credentials = new Queue<FtpCredential>(credentials);
		}

		public IList<FtpCredentialRequest> Requests { get; } = [];

		public ValueTask<FtpCredential?> ResolveAsync(FtpCredentialRequest request, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Requests.Add(request);
			return ValueTask.FromResult<FtpCredential?>(credentials.Dequeue());
		}
	}

	private sealed class CredentialCheckingSessionFactory :
		IFtpSessionFactory
	{
		private readonly IFtpSessionFactory innerFactory;

		public CredentialCheckingSessionFactory(IFtpSessionFactory innerFactory)
		{
			this.innerFactory = innerFactory;
		}

		public ValueTask<IFtpSession> ConnectAsync(FtpConnectionProfile profile, FtpCredential credential, CancellationToken cancellationToken = default)
		{
			if (credential.Password != "correct")
			{
				throw new FtpAuthenticationRequiredException(profile.ConnectionId, "Rejected test credential.");
			}

			return innerFactory.ConnectAsync(profile, credential, cancellationToken);
		}
	}
}

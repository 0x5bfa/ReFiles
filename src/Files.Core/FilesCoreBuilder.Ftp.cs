// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities.Properties;
using Files.Core.Storage.Archives;
using Files.Core.Storage.Ftp;

namespace Files.Core;

public sealed partial class FilesCoreBuilder
{
	/// <summary>Adds an FTP source with its property capability, optional local operations, and archive browsing.</summary>
	/// <param name="profile">The FTP connection profile.</param>
	/// <param name="credentialResolver">The optional credential resolver.</param>
	/// <param name="sessionFactory">The optional FTP session factory.</param>
	/// <param name="enableArchives">Whether to register archive browsing.</param>
	/// <param name="archiveCredentialResolver">The optional archive credential resolver.</param>
	/// <param name="enableOperations">Whether to register in-process execution; disable this in clients that use an external executor.</param>
	/// <returns>This builder.</returns>
	public FilesCoreBuilder AddFtpStorage(FtpConnectionProfile profile, IFtpCredentialResolver? credentialResolver = null, IFtpSessionFactory? sessionFactory = null,
		bool enableArchives = true, IArchiveCredentialResolver? archiveCredentialResolver = null, bool enableOperations = true)
	{
		ArgumentNullException.ThrowIfNull(profile);

		var source = new FtpStorageSource(profile, credentialResolver, sessionFactory);

		try
		{
			RegisterFtpStorage(source, enableOperations);
		}
		catch (Exception registrationError)
		{
			try
			{
				source.DisposeAsync().AsTask().GetAwaiter().GetResult();
			}
			catch (Exception cleanupError)
			{
				throw new AggregateException("FTP storage registration and cleanup failed.", registrationError, cleanupError);
			}

			throw;
		}

		return AddFtpCapabilities(source, enableArchives, archiveCredentialResolver);
	}

	/// <summary>Registers an existing FTP storage source and its optional capabilities.</summary>
	/// <param name="source">The FTP storage source to register.</param>
	/// <param name="enableArchives">Whether to register archive browsing.</param>
	/// <param name="archiveCredentialResolver">The optional archive credential resolver.</param>
	/// <param name="enableOperations">Whether to register in-process execution; disable this in clients that use an external executor.</param>
	/// <returns>This builder.</returns>
	public FilesCoreBuilder AddFtpStorage(FtpStorageSource source, bool enableArchives = true, IArchiveCredentialResolver? archiveCredentialResolver = null,
		bool enableOperations = true)
	{
		ArgumentNullException.ThrowIfNull(source);

		RegisterFtpStorage(source, enableOperations);

		return AddFtpCapabilities(source, enableArchives, archiveCredentialResolver);
	}

	private void RegisterFtpStorage(FtpStorageSource source, bool enableOperations)
	{
		if (enableOperations)
		{
			EnsureLocalStorageOperations();
		}

		AddStorageSource(source);
		if (enableOperations)
		{
			AddStorageOperationHandler(new FtpStorageOperationHandler(source));
		}
	}

	private FilesCoreBuilder AddFtpCapabilities(FtpStorageSource source, bool enableArchives, IArchiveCredentialResolver? archiveCredentialResolver)
	{
		Capabilities.Add<IPropertySource>(new PropertySourceFactory(new FtpPropertyReader(source)), priority: 100, origin: $"FTP:{source.Profile.ConnectionId}");

		if (enableArchives)
		{
			AddArchiveBrowsing(archiveCredentialResolver);
		}

		return this;
	}
}

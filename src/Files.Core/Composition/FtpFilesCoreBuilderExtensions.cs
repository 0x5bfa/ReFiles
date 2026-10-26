// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities.Properties;
using Files.Core.Storage.Archives;
using Files.Core.Storage.Ftp;

namespace Files.Core.Composition;

/// <summary>
/// Adds one configured FTP vertical slice to a Core runtime.
/// </summary>
public static class FtpFilesCoreBuilderExtensions
{
	/// <summary>Registers FTP storage and its optional archive and preview capabilities.</summary>
	/// <param name="builder">The composition builder.</param>
	/// <param name="profile">The FTP connection profile.</param>
	/// <param name="credentialResolver">The optional credential resolver.</param>
	/// <param name="sessionFactory">The optional FTP session factory.</param>
	/// <param name="enableArchives">Whether to register archive browsing.</param>
	/// <param name="archiveCredentialResolver">The optional archive credential resolver.</param>
	/// <param name="enableOperations">Whether to register in-process execution; disable this in clients that use an external executor.</param>
	/// <returns>The builder.</returns>
	public static FilesCoreBuilder AddFtpStorage(
		this FilesCoreBuilder builder,
		FtpConnectionProfile profile,
		IFtpCredentialResolver? credentialResolver = null,
		IFtpSessionFactory? sessionFactory = null,
		bool enableArchives = true,
		IArchiveCredentialResolver? archiveCredentialResolver = null,
		bool enableOperations = true)
	{
		ArgumentNullException.ThrowIfNull(builder);
		ArgumentNullException.ThrowIfNull(profile);

		var source = new FtpStorageSource(profile, credentialResolver, sessionFactory);

		try
		{
			RegisterStorage(builder, source, enableOperations);
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

		return AddFtpCapabilities(builder, source, enableArchives, archiveCredentialResolver);
	}

	/// <summary>Registers an existing FTP storage source and its optional capabilities.</summary>
	/// <param name="builder">The composition builder.</param>
	/// <param name="source">The FTP storage source to register.</param>
	/// <param name="enableArchives">Whether to register archive browsing.</param>
	/// <param name="archiveCredentialResolver">The optional archive credential resolver.</param>
	/// <param name="enableOperations">Whether to register in-process execution; disable this in clients that use an external executor.</param>
	/// <returns>The builder.</returns>
	public static FilesCoreBuilder AddFtpStorage(
		this FilesCoreBuilder builder,
		FtpStorageSource source,
		bool enableArchives = true,
		IArchiveCredentialResolver? archiveCredentialResolver = null,
		bool enableOperations = true)
	{
		ArgumentNullException.ThrowIfNull(builder);
		ArgumentNullException.ThrowIfNull(source);

		RegisterStorage(builder, source, enableOperations);

		return AddFtpCapabilities(builder, source, enableArchives, archiveCredentialResolver);
	}

	private static void RegisterStorage(FilesCoreBuilder builder, FtpStorageSource source, bool enableOperations)
	{
		if (enableOperations)
		{
			builder.EnsureLocalStorageOperations();
		}

		builder.AddStorageSource(source);
		if (enableOperations)
		{
			builder.AddStorageOperationHandler(new FtpStorageOperationHandler(source));
		}
	}

	private static FilesCoreBuilder AddFtpCapabilities(FilesCoreBuilder builder, FtpStorageSource source, bool enableArchives, IArchiveCredentialResolver? archiveCredentialResolver)
	{
		builder.Capabilities.Add<IPropertySource>(new PropertySourceFactory(new FtpPropertyReader(source)), priority: 100, origin: $"FTP:{source.Profile.ConnectionId}");

		if (enableArchives)
		{
			builder.AddArchiveBrowsing(archiveCredentialResolver);
		}

		return builder;
	}
}

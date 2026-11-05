// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;
using Files.Core.Capabilities.Content;
using Files.Core.Capabilities.Properties;
using Files.Core.Storage.Archives;
using Files.Core.Storage.Ftp;

namespace Files.Core;

public static partial class FilesProviderExtensions
{
	private const string FtpContentModule = "Files.Core.Ftp.Content";

	/// <summary>Adds an FTP source with its property capability, optional local operations, and archive browsing.</summary>
	/// <param name="profile">The FTP connection profile.</param>
	/// <param name="credentialResolver">The optional credential resolver.</param>
	/// <param name="sessionFactory">The optional FTP session factory.</param>
	/// <param name="enableArchives">Whether to register archive browsing.</param>
	/// <param name="archiveCredentialResolver">The optional archive credential resolver.</param>
	/// <param name="enableOperations">Whether to register in-process execution; disable this in clients that use an external executor.</param>
	/// <typeparam name="TBuilder">The runtime registration implementation.</typeparam>
	/// <param name="builder">The runtime registration target.</param>
	/// <returns>This builder.</returns>
	public static TBuilder AddFtpStorage<TBuilder>(this TBuilder builder, FtpConnectionProfile profile, IFtpCredentialResolver? credentialResolver = null, IFtpSessionFactory? sessionFactory = null,
		bool enableArchives = true, IArchiveCredentialResolver? archiveCredentialResolver = null, bool enableOperations = true) where TBuilder : class, IProviderRuntimeBuilder
	{
		ArgumentNullException.ThrowIfNull(builder);

		ArgumentNullException.ThrowIfNull(profile);

		var source = new FtpStorageSource(profile, credentialResolver, sessionFactory);

		try
		{
			RegisterFtpStorage(builder, source, enableOperations);
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
	/// <param name="source">The FTP storage source to register.</param>
	/// <param name="enableArchives">Whether to register archive browsing.</param>
	/// <param name="archiveCredentialResolver">The optional archive credential resolver.</param>
	/// <param name="enableOperations">Whether to register in-process execution; disable this in clients that use an external executor.</param>
	/// <typeparam name="TBuilder">The runtime registration implementation.</typeparam>
	/// <param name="builder">The runtime registration target.</param>
	/// <returns>This builder.</returns>
	public static TBuilder AddFtpStorage<TBuilder>(this TBuilder builder, FtpStorageSource source, bool enableArchives = true, IArchiveCredentialResolver? archiveCredentialResolver = null,
		bool enableOperations = true) where TBuilder : class, IProviderRuntimeBuilder
	{
		ArgumentNullException.ThrowIfNull(builder);

		ArgumentNullException.ThrowIfNull(source);

		RegisterFtpStorage(builder, source, enableOperations);

		return AddFtpCapabilities(builder, source, enableArchives, archiveCredentialResolver);
	}

	private static void RegisterFtpStorage<TBuilder>(TBuilder builder, FtpStorageSource source, bool enableOperations) where TBuilder : class, IProviderRuntimeBuilder
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

	private static TBuilder AddFtpCapabilities<TBuilder>(TBuilder builder, FtpStorageSource source, bool enableArchives, IArchiveCredentialResolver? archiveCredentialResolver)
		where TBuilder : class, IProviderRuntimeBuilder
	{
		AddFtpContentCapabilities(builder);
		builder.AddPropertyReader(new FtpPropertyReader(source), priority: 100, origin: $"FTP:{source.Profile.ConnectionId}");

		if (enableArchives)
		{
			builder.AddArchiveBrowsing(archiveCredentialResolver);
		}

		return builder;
	}

	private static void AddFtpContentCapabilities<TBuilder>(TBuilder builder) where TBuilder : class, IProviderRuntimeBuilder
	{
		if (builder.TryAddModule(FtpContentModule))
		{
			var contentFactory = new FtpContentSourceFactory();
			builder.Capabilities.Add<IReadableContent>(contentFactory, lifetime: CapabilityLifetime.Shared).Add<IWritableContent>(contentFactory, lifetime: CapabilityLifetime.Shared);
		}
	}
}

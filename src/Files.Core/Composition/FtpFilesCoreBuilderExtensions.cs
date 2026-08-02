// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures.Previews;
using Files.Core.ItemFeatures.Properties;
using Files.Core.Storage.Archives;
using Files.Core.Storage.Ftp;

namespace Files.Core.Composition;

/// <summary>
/// Adds one configured FTP vertical slice to a Core runtime.
/// </summary>
public static class FtpFilesCoreBuilderExtensions
{
	public static FilesCoreBuilder AddFtpStorage(
		this FilesCoreBuilder builder,
		FtpConnectionProfile profile,
		IFtpCredentialResolver? credentialResolver = null,
		IFtpSessionFactory? sessionFactory = null,
		IPreviewStreamAccessPolicy? streamPreviewPolicy = null,
		bool enablePreviews = true,
		bool enableArchives = true,
		IArchiveCredentialResolver? archiveCredentialResolver = null)
	{
		ArgumentNullException.ThrowIfNull(builder);
		ArgumentNullException.ThrowIfNull(profile);

		var source = new FtpStorageSource(profile, credentialResolver, sessionFactory);
		try
		{
			RegisterStorage(builder, source);
		}
		catch (Exception registrationError)
		{
			try
			{
				source
					.DisposeAsync()
					.AsTask()
					.GetAwaiter()
					.GetResult();
			}
			catch (Exception cleanupError)
			{
				throw new AggregateException("FTP storage registration and cleanup failed.", registrationError, cleanupError);
			}

			throw;
		}

		return AddFtpItemFeatures(builder, source, streamPreviewPolicy, enablePreviews, enableArchives, archiveCredentialResolver);
	}

	public static FilesCoreBuilder AddFtpStorage(
		this FilesCoreBuilder builder,
		FtpStorageSource source,
		IPreviewStreamAccessPolicy? streamPreviewPolicy = null,
		bool enablePreviews = true,
		bool enableArchives = true,
		IArchiveCredentialResolver? archiveCredentialResolver = null)
	{
		ArgumentNullException.ThrowIfNull(builder);
		ArgumentNullException.ThrowIfNull(source);

		RegisterStorage(builder, source);
		return AddFtpItemFeatures(builder, source, streamPreviewPolicy, enablePreviews, enableArchives, archiveCredentialResolver);
	}

	private static void RegisterStorage(FilesCoreBuilder builder, FtpStorageSource source)
	{
		builder
			.AddStorageSource(source)
			.AddStorageOperationHandler(new FtpStorageOperationHandler(source));
	}

	private static FilesCoreBuilder AddFtpItemFeatures(
		FilesCoreBuilder builder,
		FtpStorageSource source,
		IPreviewStreamAccessPolicy? streamPreviewPolicy,
		bool enablePreviews,
		bool enableArchives,
		IArchiveCredentialResolver? archiveCredentialResolver)
	{
		builder.ItemFeatures.Add<IPropertySource>(
			new PropertySourceFactory(new FtpPropertyReader(source)),
			priority: 100,
			origin: $"FTP:{source.Profile.ConnectionId}");

		if (enablePreviews)
		{
			builder.AddDefaultStreamPreviews(streamPreviewPolicy ?? AllowPreviewStreamAccessPolicy.Instance);
		}

		if (enableArchives)
		{
			builder.AddArchiveBrowsing(archiveCredentialResolver);
		}

		return builder;
	}
}

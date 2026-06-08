// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.CompilerServices;
using OwlCore.Storage;

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Resolves one configured FTP or FTPS connection.
/// </summary>
public sealed class FtpStorageSource : IStorageSource
{
	public const string DefaultSourceType = "ftp";
	public const string FtpAddressScheme = "ftp";
	public const string ExplicitTlsAddressScheme = "ftpes";
	public const string ImplicitTlsAddressScheme = "ftps";

	private readonly FtpConnection connection;
	private readonly FtpItemResolver resolver;
	private readonly FtpStorableFactory storableFactory;
	private readonly string canonicalHost;
	private int isDisposed;

	public FtpStorageSource(
		FtpConnectionProfile profile,
		IFtpCredentialResolver? credentialResolver = null,
		IFtpSessionFactory? sessionFactory = null,
		StorageSourceId? sourceId = null)
	{
		ArgumentNullException.ThrowIfNull(profile);

		Profile = profile;
		SourceId = sourceId
			?? new StorageSourceId(
				$"{DefaultSourceType}:{profile.ConnectionId}");
		connection = new FtpConnection(
			profile,
			credentialResolver
				?? AnonymousFtpCredentialResolver.Instance,
			sessionFactory
				?? FluentFtpSessionFactory.Instance);
		resolver = new FtpItemResolver(profile, connection);
		storableFactory = new FtpStorableFactory(
			this,
			resolver,
			connection);
		canonicalHost = GetCanonicalHost(profile.Host);
	}

	public StorageSourceId SourceId { get; }

	public string SourceType => DefaultSourceType;

	public string DisplayName => Profile.DisplayName;

	public FtpConnectionProfile Profile { get; }

	public string AddressScheme => Profile.SecurityMode switch
	{
		FtpSecurityMode.Plain => FtpAddressScheme,
		FtpSecurityMode.ExplicitTls => ExplicitTlsAddressScheme,
		FtpSecurityMode.ImplicitTls => ImplicitTlsAddressScheme,
		_ => throw new InvalidOperationException(
			"Unsupported FTP security mode."),
	};

	internal FtpConnection Connection => connection;

	internal FtpItemResolver Resolver => resolver;

	internal FtpStorable CreateStorable(FtpEntryInfo entry)
	{
		return storableFactory.Create(entry);
	}

	public async IAsyncEnumerable<IFolder> GetRootsAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();

		var root = await storableFactory
			.ResolveAsync(Profile.RootPath, cancellationToken)
			.ConfigureAwait(false);
		if (root is not FtpFolder folder)
		{
			throw new InvalidOperationException(
				"The configured FTP root did not resolve to a folder.");
		}

		yield return folder;
	}

	public bool CanResolve(StorageAddress address)
	{
		ArgumentNullException.ThrowIfNull(address);
		return TryGetPath(address, out _);
	}

	public async ValueTask<IStorable> ResolveAsync(
		StorageAddress address,
		CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(address);

		if (!TryGetPath(address, out var path))
		{
			throw new ArgumentException(
				"The address does not belong to this FTP connection.",
				nameof(address));
		}

		return await storableFactory
			.ResolveAsync(path, cancellationToken)
			.ConfigureAwait(false);
	}

	public async ValueTask<IStorable> ResolveAsync(
		StorableReference reference,
		CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(reference);
		if (reference.SourceId != SourceId)
		{
			throw new ArgumentException(
				$"Reference belongs to storage source '{reference.SourceId}'.",
				nameof(reference));
		}

		var path = FtpPath.Parse(reference.ItemId);
		return await storableFactory
			.ResolveAsync(path, cancellationToken)
			.ConfigureAwait(false);
	}

	public StorageAddress CreateAddress(FtpPath path)
	{
		ArgumentNullException.ThrowIfNull(path);
		if (!path.IsWithin(
			Profile.RootPath,
			Profile.PathComparer))
		{
			throw new ArgumentException(
				"The FTP path is outside the configured root.",
				nameof(path));
		}

		var host = Profile.Host.Contains(':')
			? $"[{Profile.Host}]"
			: Profile.Host;
		return new StorageAddress(
			AddressScheme,
			$"//{host}:{Profile.Port}{path.ToEscapedUriPath()}");
	}

	public StorableReference CreateReference(FtpPath path)
	{
		return new StorableReference(
			SourceId,
			path.Value,
			CreateAddress(path));
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref isDisposed, 1) is not 0)
		{
			return;
		}

		await connection.DisposeAsync().ConfigureAwait(false);
		GC.SuppressFinalize(this);
	}

	private bool TryGetPath(
		StorageAddress address,
		out FtpPath path)
	{
		path = FtpPath.Root;
		if (!address.Scheme.Equals(
			AddressScheme,
			StringComparison.OrdinalIgnoreCase)
			|| !Uri.TryCreate(
				$"{address.Scheme}:{address.Value}",
				UriKind.Absolute,
				out var uri)
			|| uri.Port != Profile.Port
			|| !string.IsNullOrEmpty(uri.UserInfo)
			|| !string.IsNullOrEmpty(uri.Query)
			|| !string.IsNullOrEmpty(uri.Fragment)
			|| !canonicalHost.Equals(
				uri.IdnHost,
				StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		try
		{
			var escapedPath = uri.GetComponents(
				UriComponents.Path,
				UriFormat.UriEscaped);
			path = FtpPath.ParseEscapedUriPath(
				escapedPath.StartsWith('/')
					? escapedPath
					: $"/{escapedPath}");
			return path.IsWithin(
				Profile.RootPath,
				Profile.PathComparer);
		}
		catch (ArgumentException)
		{
			return false;
		}
		catch (UriFormatException)
		{
			return false;
		}
	}

	private string GetCanonicalHost(string host)
	{
		var formattedHost = host.Contains(':')
			? $"[{host}]"
			: host;
		var endpoint = new Uri(
			$"{AddressScheme}://{formattedHost}:{Profile.Port}/",
			UriKind.Absolute);
		return endpoint.IdnHost;
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(
			Volatile.Read(ref isDisposed) is not 0,
			this);
	}
}

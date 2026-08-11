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
	/// <summary>Gets the default storage source type.</summary>
	public const string DefaultSourceType = "ftp";

	/// <summary>Gets the plain FTP address scheme.</summary>
	public const string FtpAddressScheme = "ftp";

	/// <summary>Gets the explicit TLS address scheme.</summary>
	public const string ExplicitTlsAddressScheme = "ftpes";

	/// <summary>Gets the implicit TLS address scheme.</summary>
	public const string ImplicitTlsAddressScheme = "ftps";

	private readonly FtpConnection _connection;

	private readonly FtpItemResolver _resolver;

	private readonly FtpStorableFactory _storableFactory;

	private readonly string _canonicalHost;

	private int _isDisposed;

	/// <summary>Gets the stable storage source identifier.</summary>
	public StorageSourceId SourceId { get; }

	/// <summary>Gets the source type.</summary>
	public string SourceType => DefaultSourceType;

	/// <summary>Gets the configured display name.</summary>
	public string DisplayName => Profile.DisplayName;

	/// <summary>Gets the connection profile.</summary>
	public FtpConnectionProfile Profile { get; }

	/// <summary>Gets the address scheme for this connection.</summary>
	public string AddressScheme => Profile.SecurityMode switch
	{
		FtpSecurityMode.Plain => FtpAddressScheme,
		FtpSecurityMode.ExplicitTls => ExplicitTlsAddressScheme,
		FtpSecurityMode.ImplicitTls => ImplicitTlsAddressScheme,
		_ => throw new InvalidOperationException("Unsupported FTP security mode."),
	};

	internal FtpConnection Connection => _connection;

	internal FtpItemResolver Resolver => _resolver;

	/// <summary>Initializes an FTP storage source.</summary>
	/// <param name="profile">The connection profile.</param>
	/// <param name="credentialResolver">The optional credential resolver.</param>
	/// <param name="sessionFactory">The optional FTP session factory.</param>
	/// <param name="sourceId">The optional stable source identifier.</param>
	public FtpStorageSource(FtpConnectionProfile profile, IFtpCredentialResolver? credentialResolver = null, IFtpSessionFactory? sessionFactory = null, StorageSourceId? sourceId = null)
	{
		ArgumentNullException.ThrowIfNull(profile);

		Profile = profile;
		SourceId = sourceId
			?? new StorageSourceId($"{DefaultSourceType}:{profile.ConnectionId}");
		_connection = new FtpConnection(profile, credentialResolver ?? AnonymousFtpCredentialResolver.Instance, sessionFactory ?? FluentFtpSessionFactory.Instance);
		_resolver = new FtpItemResolver(profile, _connection);
		_storableFactory = new FtpStorableFactory(this, _resolver, _connection);
		_canonicalHost = GetCanonicalHost(profile.Host);
	}

	internal FtpStorable CreateStorable(FtpEntryInfo entry)
	{
		return _storableFactory.Create(entry);
	}

	/// <summary>Enumerates the configured FTP root folder.</summary>
	/// <param name="cancellationToken">The token used to cancel enumeration.</param>
	/// <returns>An asynchronous sequence containing the root folder.</returns>
	public async IAsyncEnumerable<IFolder> GetRootsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();

		var root = await _storableFactory.ResolveAsync(Profile.RootPath, cancellationToken).ConfigureAwait(false);
		if (root is not FtpFolder folder)
		{
			throw new InvalidOperationException("The configured FTP root did not resolve to a folder.");
		}

		yield return folder;
	}

	/// <summary>Determines whether this source can resolve an address.</summary>
	/// <param name="address">The address to inspect.</param>
	/// <returns><see langword="true"/> when the address belongs to this source.</returns>
	public bool CanResolve(StorageAddress address)
	{
		ArgumentNullException.ThrowIfNull(address);

		return TryGetPath(address, out _);
	}

	/// <summary>Resolves an FTP storage address.</summary>
	/// <param name="address">The address to resolve.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The resolved item.</returns>
	public async ValueTask<IStorable> ResolveAsync(StorageAddress address, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(address);

		if (!TryGetPath(address, out var path))
		{
			throw new ArgumentException("The address does not belong to this FTP connection.", nameof(address));
		}

		return await _storableFactory.ResolveAsync(path, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Resolves a stable FTP item reference.</summary>
	/// <param name="reference">The reference to resolve.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The resolved item.</returns>
	public async ValueTask<IStorable> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(reference);

		if (reference.SourceId != SourceId)
		{
			throw new ArgumentException($"Reference belongs to storage source '{reference.SourceId}'.", nameof(reference));
		}

		var path = FtpPath.Parse(reference.ItemId);

		return await _storableFactory.ResolveAsync(path, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Creates an address for an FTP path.</summary>
	/// <param name="path">The FTP path.</param>
	/// <returns>The storage address.</returns>
	public StorageAddress CreateAddress(FtpPath path)
	{
		ArgumentNullException.ThrowIfNull(path);

		if (!path.IsWithin(Profile.RootPath, Profile.PathComparer))
		{
			throw new ArgumentException("The FTP path is outside the configured root.", nameof(path));
		}

		var host = Profile.Host.Contains(':')
			? $"[{Profile.Host}]"
			: Profile.Host;

		return new StorageAddress(
			AddressScheme,
			$"//{host}:{Profile.Port}{path.ToEscapedUriPath()}");
	}

	/// <summary>Creates a stable reference for an FTP path.</summary>
	/// <param name="path">The FTP path.</param>
	/// <returns>The stable item reference.</returns>
	public StorableReference CreateReference(FtpPath path)
	{
		return new StorableReference(SourceId, path.Value, CreateAddress(path));
	}

	/// <summary>Disposes the FTP connection.</summary>
	/// <returns>A value task that represents the disposal operation.</returns>
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		await _connection.DisposeAsync().ConfigureAwait(false);
		GC.SuppressFinalize(this);
	}

	private bool TryGetPath(StorageAddress address, out FtpPath path)
	{
		path = FtpPath.Root;
		if (!address.Scheme.Equals(AddressScheme, StringComparison.OrdinalIgnoreCase)
			|| !Uri.TryCreate($"{address.Scheme}:{address.Value}", UriKind.Absolute, out var uri)
			|| uri.Port != Profile.Port
			|| !string.IsNullOrEmpty(uri.UserInfo)
			|| !string.IsNullOrEmpty(uri.Query)
			|| !string.IsNullOrEmpty(uri.Fragment)
			|| !_canonicalHost.Equals(uri.IdnHost, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		try
		{
			var escapedPath = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
			path = FtpPath.ParseEscapedUriPath(escapedPath.StartsWith('/') ? escapedPath : $"/{escapedPath}");

			return path.IsWithin(Profile.RootPath, Profile.PathComparer);
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
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) is not 0, this);

	}
}

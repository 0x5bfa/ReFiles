// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Storage.Ftp;
using Files.Core.Storage.Windows;

namespace Files.Core;

/// <summary>Owns operation-host execution and typed provider administration.</summary>
public interface IOperationHostRuntime : IFilesCoreRuntime
{
	/// <summary>Adds an owned FTP connection with a borrowed credential resolver.</summary>
	/// <param name="profile">The connection settings.</param>
	/// <param name="credentialResolver">The borrowed credential provider.</param>
	/// <returns>The registered source identity.</returns>
	StorageSourceId RegisterFtpConnection(FtpConnectionProfile profile, IFtpCredentialResolver credentialResolver);

	/// <summary>Gets Shell New menu choices for a destination.</summary>
	/// <param name="folderPath">The destination folder.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The current menu choices.</returns>
	Task<IReadOnlyList<WindowsShellNewItem>> GetNewItemsAsync(string folderPath, CancellationToken cancellationToken = default);

	/// <summary>Invokes a current Shell New menu choice.</summary>
	/// <param name="folderPath">The destination folder.</param>
	/// <param name="commandOffset">The current command offset.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>Whether invocation succeeded.</returns>
	Task<bool> InvokeNewItemAsync(string folderPath, uint commandOffset, CancellationToken cancellationToken = default);

	/// <summary>Applies filesystem attributes to resolved Windows storage items.</summary>
	/// <param name="references">The captured storage item identities.</param>
	/// <param name="readOnly">The optional read-only state.</param>
	/// <param name="hidden">The optional hidden state.</param>
	/// <param name="archive">The optional archive state.</param>
	/// <param name="indexed">The optional indexing state.</param>
	/// <param name="compressed">The optional compression state.</param>
	/// <param name="encrypted">The optional encryption state.</param>
	/// <param name="applyToContents">Whether to include folder contents.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	Task ApplyFileAttributesAsync(IReadOnlyList<StorableReference> references, bool? readOnly, bool? hidden, bool? archive, bool? indexed, bool? compressed, bool? encrypted,
		bool applyToContents, CancellationToken cancellationToken = default);
}

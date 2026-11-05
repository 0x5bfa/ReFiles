// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Previews;
using Files.Core.Capabilities.Properties;
using Files.Core.Data;
using Files.Core.Storage;
using Files.Core.Storage.Archives;
using Files.Core.Storage.Transfers;
using Microsoft.Extensions.Logging;

namespace Files.Core;

/// <summary>Allows provider modules to configure a runtime through explicit storage and adaptation contracts.</summary>
public interface IProviderRuntimeBuilder
{
	/// <summary>Gets the capability registration surface.</summary>
	ICapabilityBuilder Capabilities { get; }
	/// <summary>Gets the borrowed logging factory.</summary>
	ILoggerFactory LoggerFactory { get; }

	/// <summary>Registers an owned storage source.</summary>
	/// <param name="source">The owned source.</param>
	void AddStorageSource(IStorageSource source);
	/// <summary>Registers an operation handler.</summary>
	/// <param name="handler">The operation handler.</param>
	void AddStorageOperationHandler(IStorageOperationHandler handler);
	/// <summary>Registers a browse adapter over the configured workspace.</summary>
	/// <param name="factory">The handler factory.</param>
	void AddBrowseLocationHandler(Func<IStorageWorkspace, IBrowseLocationHandler> factory);
	/// <summary>Registers a property reader with runtime-provided item adaptation.</summary>
	/// <param name="reader">The borrowed reader.</param>
	/// <param name="priority">The contribution priority.</param>
	/// <param name="origin">The diagnostic origin.</param>
	void AddPropertyReader(IPropertyReader reader, int priority, string origin);
	/// <summary>Registers a preview loader with runtime-provided item adaptation.</summary>
	/// <param name="loader">The borrowed loader.</param>
	/// <param name="priority">The contribution priority.</param>
	/// <param name="origin">The diagnostic origin.</param>
	void AddPreviewLoader(IPreviewLoader loader, int priority, string origin);
	/// <summary>Registers an optional provider-specific service over the configured workspace.</summary>
	/// <typeparam name="TService">The provider-facing service contract.</typeparam>
	/// <param name="factory">Creates a borrowed integration service. Register its resource owner with <see cref="Own"/> before building.</param>
	void SetProviderServiceFactory<TService>(Func<IStorageWorkspace, TService> factory) where TService : class;
	/// <summary>Registers generic archive browsing using a provider capability factory.</summary>
	/// <param name="readers">The reader selection.</param>
	/// <param name="credentials">The optional borrowed credential resolver.</param>
	void ConfigureArchiveBrowsing(ArchiveBackendSelector readers, IArchiveCredentialResolver? credentials);
	/// <summary>Registers generic transfers using destination adapters supplied by providers.</summary>
	/// <param name="readers">The archive reader selection.</param>
	/// <param name="credentials">The optional execution-owned credentials.</param>
	/// <param name="imports">Creates destination adapters for the configured workspace.</param>
	void ConfigureStorageTransfers(ArchiveBackendSelector readers, IArchiveCredentialResolver? credentials, Func<IStorageWorkspace, IReadOnlyList<IStorageImportProvider>> imports);
	/// <summary>Transfers ownership of a process service to the runtime.</summary>
	/// <param name="service">The owned process service.</param>
	void Own(IAsyncDisposable service);
	/// <summary>Checks that local execution is compatible with the configured runtime.</summary>
	void EnsureLocalStorageOperations();
	/// <summary>Registers a module identity once per runtime builder.</summary>
	/// <param name="moduleId">The stable module identity.</param>
	/// <returns>Whether this is the first registration.</returns>
	bool TryAddModule(string moduleId);
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core;

/// <summary>Exposes explicitly registered services to provider integration code, outside the consumer runtime contract.</summary>
public interface IProviderRuntimeServices
{
	/// <summary>Gets a provider-specific service registered during runtime composition.</summary>
	/// <typeparam name="TService">The provider-facing contract.</typeparam>
	/// <returns>The borrowed service, or null when it was not registered.</returns>
	TService? GetProviderService<TService>() where TService : class;
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Settings;

/// <summary>Persists application settings documents within one configured settings directory.</summary>
public interface ISettingsStorage
{
	/// <summary>Reads an existing settings document.</summary>
	/// <param name="fileName">A filename within the configured settings directory.</param>
	/// <param name="cancellationToken">Cancels reading.</param>
	/// <returns>The document, or null when it does not exist.</returns>
	Task<string?> ReadTextAsync(string fileName, CancellationToken cancellationToken = default);
	/// <summary>Atomically replaces a settings document with a complete UTF-8 document.</summary>
	/// <param name="fileName">A filename within the configured settings directory.</param>
	/// <param name="text">The complete document.</param>
	/// <param name="cancellationToken">Cancels before the atomic commit.</param>
	/// <returns>The persistence operation.</returns>
	Task WriteTextAsync(string fileName, string text, CancellationToken cancellationToken = default);
}

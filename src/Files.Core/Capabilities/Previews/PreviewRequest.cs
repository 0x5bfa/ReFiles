// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Windows;
using OwlCore.Storage;

namespace Files.Core.Capabilities.Previews;

/// <summary>Specifies the limits and hydration policy for a preview request.</summary>
public enum PreviewHydrationPolicy
{
	/// <summary>Use content that is already available locally.</summary>
	LocalOnly,
	/// <summary>Allow the preview provider to hydrate content.</summary>
	AllowHydration,
}

/// <summary>Describes the limits applied to a preview request.</summary>
public sealed record PreviewRequest
{
	internal PreviewTrustAuthorization? TrustAuthorization { get; }

	internal PreviewTrustPolicy TrustPolicy => TrustAuthorization is null ? PreviewTrustPolicy.Default : PreviewTrustPolicy.AllowUntrusted;

	/// <summary>Gets the maximum number of bytes that may be read.</summary>
	public long? MaximumBytes { get; }

	/// <summary>Gets the policy controlling content hydration.</summary>
	public PreviewHydrationPolicy HydrationPolicy { get; }

	/// <summary>Initializes a preview request.</summary>
	/// <param name="maximumBytes">The maximum number of bytes to read.</param>
	/// <param name="hydrationPolicy">The content hydration policy.</param>
	public PreviewRequest(long? maximumBytes = null, PreviewHydrationPolicy hydrationPolicy = PreviewHydrationPolicy.LocalOnly)
		: this(maximumBytes, hydrationPolicy, null)
	{
	}

	internal PreviewRequest(long? maximumBytes, PreviewHydrationPolicy hydrationPolicy, PreviewTrustAuthorization? trustAuthorization)
	{
		if (maximumBytes is not null)
		{
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes.Value);
		}

		if (hydrationPolicy is not PreviewHydrationPolicy.LocalOnly and not PreviewHydrationPolicy.AllowHydration)
		{
			throw new ArgumentOutOfRangeException(nameof(hydrationPolicy));
		}

		MaximumBytes = maximumBytes;
		HydrationPolicy = hydrationPolicy;
		TrustAuthorization = trustAuthorization;
	}
}

internal sealed class PreviewTrustAuthorization
{
	private readonly StorableReference _reference;
	private readonly IStorable _coreModel;
	private readonly string? _fileSystemPath;

	internal PreviewTrustAuthorization(IStorableModel target)
	{
		ArgumentNullException.ThrowIfNull(target);

		_reference = target.Reference;
		_coreModel = target.GetCoreModel();
		_fileSystemPath = (_coreModel as IWindowsStorable)?.FileSystemPath;
	}

	internal PreviewTrustAuthorization(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		_reference = context.Reference;
		_coreModel = context.CoreModel;
		_fileSystemPath = (context.CoreModel as IWindowsStorable)?.FileSystemPath;
	}

	internal bool AppliesTo(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (!context.Reference.Equals(_reference))
		{
			return false;
		}

		if (ReferenceEquals(context.CoreModel, _coreModel))
		{
			return true;
		}

		return _fileSystemPath is not null && context.CoreModel is IWindowsStorable windowsItem && StringComparer.OrdinalIgnoreCase.Equals(_fileSystemPath, windowsItem.FileSystemPath);
	}
}

internal enum PreviewTrustPolicy
{
	Default,
	AllowUntrusted,
}

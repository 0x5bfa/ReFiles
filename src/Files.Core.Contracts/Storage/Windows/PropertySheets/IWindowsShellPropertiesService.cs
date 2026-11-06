// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Models;
using Files.Core.Capabilities.Thumbnails;

namespace Files.Core.Storage.Windows;

/// <summary>Reads Shell property page data and opens requested property sheets.</summary>
public interface IWindowsShellPropertiesService
{
	/// <summary>Gets the Windows Shell property pages applicable to a selection.</summary>
	/// <param name="selection">The selected Windows Shell item references.</param>
	/// <param name="cancellationToken">The token used to cancel selection resolution.</param>
	/// <returns>Apartment-neutral descriptions of the ReFiles pages that apply to the selection.</returns>
	Task<IReadOnlyList<WindowsShellPropertyPage>> GetPropertyPagesAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken = default);

	/// <summary>Gets the native data used to render the Windows property pages for a selection.</summary>
	/// <param name="selection">The selected Windows Shell item references.</param>
	/// <param name="cancellationToken">The token used to cancel selection resolution and property retrieval.</param>
	/// <returns>Apartment-neutral page data, or <see langword="null"/> when the selection cannot be resolved.</returns>
	Task<WindowsShellPropertySheetData?> GetPropertySheetDataAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken = default);

	/// <summary>Gets the native data used to render one Windows property page for a selection.</summary>
	/// <param name="selection">The selected Windows Shell item references.</param>
	/// <param name="kind">The property page to read.</param>
	/// <param name="cancellationToken">The token used to cancel selection resolution and property retrieval.</param>
	/// <returns>Apartment-neutral data for the requested page, or <see langword="null"/> when the page does not apply or the selection cannot be resolved.</returns>
	Task<WindowsShellPropertySheetData?> GetPropertyPageDataAsync(IReadOnlyList<StorableReference> selection, WindowsShellPropertyPageKind kind, CancellationToken cancellationToken = default);

	/// <summary>Gets the Shell description and icon shown on the General property page for a single item.</summary>
	/// <param name="item">The borrowed item model whose composed thumbnail capability supplies the icon.</param>
	/// <param name="cancellationToken">The token used to cancel selection resolution and icon extraction.</param>
	/// <returns>The description and optional icon, or empty values when the item cannot be resolved.</returns>
	Task<(string? Description, ThumbnailResult? Icon)> GetGeneralPropertiesAsync(IStorableModel item, CancellationToken cancellationToken = default);

	/// <summary>Opens the Shell property sheet for a selection without constructing an <c>IContextMenu</c>.</summary>
	/// <param name="selection">The selected Windows Shell item references.</param>
	/// <param name="cancellationToken">The token used to cancel preparation.</param>
	/// <returns><see langword="true"/> when the property sheet request was accepted.</returns>
	Task<bool> ShowShellPropertiesAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken = default);
}

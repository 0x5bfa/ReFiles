// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Diagnostics;
using System.IO;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Storage;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Storage.Windows;

/// <summary>Reads Windows Shell property pages and invokes the native property sheet.</summary>
public sealed class WindowsShellPropertiesService
{
	private readonly WindowsStorageSource _source;
	private readonly WindowsShellThumbnailBackend _thumbnailBackend = new();

	/// <summary>Initializes the property service for a Windows storage source.</summary>
	/// <param name="source">The source used to resolve the selected items.</param>
	public WindowsShellPropertiesService(WindowsStorageSource source)
	{
		ArgumentNullException.ThrowIfNull(source);

		_source = source;
	}

	/// <summary>Gets the Windows Shell property pages applicable to a selection.</summary>
	/// <param name="selection">The selected Windows Shell item references.</param>
	/// <param name="cancellationToken">The token used to cancel selection resolution.</param>
	/// <returns>Apartment-neutral descriptions of the ReFiles pages that apply to the selection.</returns>
	public async Task<IReadOnlyList<WindowsShellPropertyPage>> GetPropertyPagesAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);

		if (selection.Count is 0 || await ResolveSelectionAsync(selection, cancellationToken).ConfigureAwait(false) is not { } resolvedSelection)
		{
			return [];
		}

		return WindowsShellPropertyPageEnumerator.GetPages(resolvedSelection);
	}

	/// <summary>Gets the native data used to render the Windows property pages for a selection.</summary>
	/// <param name="selection">The selected Windows Shell item references.</param>
	/// <param name="cancellationToken">The token used to cancel selection resolution and property retrieval.</param>
	/// <returns>Apartment-neutral page data, or <see langword="null"/> when the selection cannot be resolved.</returns>
	public async Task<WindowsShellPropertySheetData?> GetPropertySheetDataAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);

		if (selection.Count is 0 || await ResolveSelectionAsync(selection, cancellationToken).ConfigureAwait(false) is not { } resolvedSelection)
		{
			return null;
		}

		return await _source.Scheduler.InvokeConcurrentAsync(
			() => ReadPropertySheetDataOnCurrentSta(resolvedSelection, WindowsShellPropertyPageEnumerator.GetPages(resolvedSelection), cancellationToken), cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Gets the native data used to render one Windows property page for a selection.</summary>
	/// <param name="selection">The selected Windows Shell item references.</param>
	/// <param name="kind">The property page to read.</param>
	/// <param name="cancellationToken">The token used to cancel selection resolution and property retrieval.</param>
	/// <returns>Apartment-neutral data for the requested page, or <see langword="null"/> when the page does not apply or the selection cannot be resolved.</returns>
	public async Task<WindowsShellPropertySheetData?> GetPropertyPageDataAsync(
		IReadOnlyList<StorableReference> selection, WindowsShellPropertyPageKind kind, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);

		if (selection.Count is 0 || await ResolveSelectionAsync(selection, cancellationToken).ConfigureAwait(false) is not { } resolvedSelection)
		{
			return null;
		}

		var page = WindowsShellPropertyPageEnumerator.GetPages(resolvedSelection).FirstOrDefault(page => page.Kind == kind);
		if (page is null)
		{
			return null;
		}

		return await _source.Scheduler.InvokeConcurrentAsync(() => ReadPropertySheetDataOnCurrentSta(resolvedSelection, [page], cancellationToken), cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Gets the Shell description and icon shown on the General property page for a single item.</summary>
	/// <param name="selection">The selected Windows Shell item references.</param>
	/// <param name="cancellationToken">The token used to cancel selection resolution and icon extraction.</param>
	/// <returns>The description and icon, or empty values when the selection cannot be resolved to one item.</returns>
	public async Task<(string? Description, ThumbnailResult? Icon)> GetGeneralPropertiesAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);

		if (selection.Count is not 1 || await ResolveSelectionAsync(selection, cancellationToken).ConfigureAwait(false) is not { } resolvedSelection)
		{
			return (null, null);
		}

		var locator = resolvedSelection.Locators[0];

		return await _source.Scheduler.InvokeConcurrentAsync(() =>
		{
			var shellItem = WindowShellHelpers.TryResolveItem(locator);

			return shellItem is null ? default : ReadGeneralProperties(shellItem, locator, cancellationToken);
		}, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Opens the Shell property sheet for a selection without constructing an <c>IContextMenu</c>.</summary>
	/// <param name="selection">The selected Windows Shell item references.</param>
	/// <param name="cancellationToken">The token used to cancel preparation.</param>
	/// <returns><see langword="true"/> when the property sheet request was accepted.</returns>
	public async Task<bool> ShowShellPropertiesAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);

		if (selection.Count is 0 || await ResolveSelectionAsync(selection, cancellationToken).ConfigureAwait(false) is not { } resolvedSelection)
		{
			return false;
		}

		return await _source.Scheduler.InvokeOperationAsync(() => ShowShellPropertiesOnCurrentSta(resolvedSelection), cancellationToken).ConfigureAwait(false);
	}

	private static bool ShowShellPropertiesOnCurrentSta(WindowsShellResolvedSelection selection)
	{
		var shellItemArray = WindowShellHelpers.CreateShellItemArray(selection.Locators);
		var bindResult = shellItemArray.BindToHandler<IDataObject>(null, PInvoke.BHID_DataObject, out var dataObject);

		return bindResult.Succeeded && dataObject is not null && PInvoke.SHMultiFileProperties(dataObject, 0).Succeeded;
	}

	private static WindowsShellPropertySheetData ReadPropertySheetDataOnCurrentSta(
		WindowsShellResolvedSelection selection, IReadOnlyList<WindowsShellPropertyPage> pages, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var shellItemArray = WindowShellHelpers.CreateShellItemArray(selection.Locators);
		if (shellItemArray.GetItemAt(0, out var primaryItem).Failed || primaryItem is null)
		{
			return WindowsShellPropertySheetReader.CreateEmpty(pages);
		}

		return WindowsShellPropertySheetReader.Read(primaryItem, selection, pages, cancellationToken);
	}

	private (string? Description, ThumbnailResult? Icon) ReadGeneralProperties(IShellItem shellItem, WindowsItemLocator locator, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var description = ReadShellString(shellItem, "System.FileDescription");
		if (string.IsNullOrWhiteSpace(description) && ReadShellString(shellItem, "System.Link.TargetParsingPath") is { Length: > 0 } targetPath)
		{
			description = ReadFileDescription(targetPath);
		}

		if (string.IsNullOrWhiteSpace(description))
		{
			description = ReadShellString(shellItem, "System.Comment");
		}

		var payload = _thumbnailBackend.GetThumbnail(shellItem, locator, new ThumbnailRequest(48, ThumbnailMode.Icon), cancellationToken);
		var icon = payload is null ? null : new ThumbnailResult(payload.Content, payload.ContentType, payload.IsFallback, payload.Format, payload.PixelWidth, payload.PixelHeight);

		return (description, icon);
	}

	private static string? ReadFileDescription(string path)
	{
		try
		{
			return File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).FileDescription : null;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static unsafe string? ReadShellString(IShellItem shellItem, string propertyId)
	{
		if (shellItem is not IShellItem2 shellItem2 || PInvoke.PSGetPropertyKeyFromName(propertyId, out var key).Failed)
		{
			return null;
		}

		PWSTR value = default;
		try
		{
			var hr = shellItem2.GetString(key, out value);

			return hr.Succeeded && value.Value is not null ? value.ToString() : null;
		}
		finally
		{
			PInvoke.CoTaskMemFree(value.Value);
		}
	}

	private Task<WindowsShellResolvedSelection?> ResolveSelectionAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken)
	{
		return WindowsShellMenuSelectionResolver.ResolveAsync(_source, selection, cancellationToken);
	}
}

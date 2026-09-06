// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text.Json;
using System.Text.Json.Serialization;
using Files.Core.ViewSettings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Windows;

internal static unsafe partial class WindowsShellViewSettingsPropertyBag
{
	internal const int CurrentSchemaVersion = 1;

	private const int MissingRegistryValueHResult = unchecked((int)0x80070002);
	private const int MissingPropertyHResult = unchecked((int)0x80070057);
	private const int MissingShellValueHResult = unchecked((int)0x80004005);

	// Explorer's reserved "Shell" bag uses a private payload; ReFiles owns this app-specific bag.
	private const string PropertyBagName = "ReFiles.ViewSettings";
	private const string PropertyName = "ViewSettings";
	private const uint PerUserPropertyBagFlag = 0x00000001;
	private const uint PerFolderPropertyBagFlag = 0x00000004;
	private const uint NoAutoDefaultsPropertyBagFlag = 0x80000000;
	private const uint PropertyBagFlags = PerUserPropertyBagFlag | PerFolderPropertyBagFlag | NoAutoDefaultsPropertyBagFlag;

	internal static BrowseViewSettingsOverride? Read(IShellItem shellItem, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(shellItem);

		cancellationToken.ThrowIfCancellationRequested();

		var hr = CreatePropertyBag(shellItem, out var propertyBag);
		hr.ThrowOnFailure();
		if (propertyBag is null)
		{
			throw new COMException("The Shell view-state property bag did not provide IPropertyBag.", HRESULT.E_NOINTERFACE);
		}

		cancellationToken.ThrowIfCancellationRequested();

		ComVariant value = default;
		try
		{
			hr = propertyBag.Read(PropertyName, ref value, null!);
			// With VT_EMPTY input, CViewStatePropertyBag reports an unwritten named value as E_FAIL on current Windows builds.
			if (hr.Value is MissingShellValueHResult or MissingPropertyHResult or MissingRegistryValueHResult)
			{
				return null;
			}

			hr.ThrowOnFailure();
			if (value.VarType is not VarEnum.VT_BSTR and not VarEnum.VT_LPWSTR)
			{
				throw new InvalidDataException("The Shell view-state property has an unsupported value type.");
			}

			cancellationToken.ThrowIfCancellationRequested();

			var json = value.VarType is VarEnum.VT_BSTR ? value.As<string>() : Marshal.PtrToStringUni(value.GetRawDataRef<nint>());
			// The registry-backed bag can expose an allocated BSTR tail beyond its first terminator.
			var terminatorIndex = json?.IndexOf('\0') ?? -1;
			if (terminatorIndex >= 0)
			{
				json = json[..terminatorIndex];
			}

			var settings = string.IsNullOrWhiteSpace(json) ? null : Deserialize(json);
			if (settings is null)
			{
				throw new InvalidDataException("The Shell view-state property does not contain a supported payload.");
			}

			return settings;
		}
		finally
		{
			value.Dispose();
		}
	}

	internal static void Write(IShellItem shellItem, BrowseViewSettingsOverride? settings, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(shellItem);

		cancellationToken.ThrowIfCancellationRequested();

		var json = settings is null ? null : Serialize(settings);
		var hr = CreatePropertyBag(shellItem, out var propertyBag);
		hr.ThrowOnFailure();
		if (propertyBag is null)
		{
			throw new COMException("The Shell view-state property bag did not provide IPropertyBag.", HRESULT.E_NOINTERFACE);
		}

		cancellationToken.ThrowIfCancellationRequested();

		if (json is null)
		{
			PInvoke.PSPropertyBag_Delete(propertyBag, PropertyName).ThrowOnFailure();

			return;
		}

		var value = ComVariant.Create(json);
		try
		{
			propertyBag.Write(PropertyName, in value).ThrowOnFailure();
		}
		finally
		{
			value.Dispose();
		}
	}

	internal static string Serialize(BrowseViewSettingsOverride settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		var document = new ViewSettingsDocument
		{
			SchemaVersion = CurrentSchemaVersion,
			Settings = ToDocument(settings),
		};

		return JsonSerializer.Serialize(document, ShellViewSettingsJsonSerializerContext.Default.ViewSettingsDocument);
	}

	internal static BrowseViewSettingsOverride? Deserialize(string json)
	{
		ArgumentNullException.ThrowIfNull(json);

		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		ViewSettingsDocument? document;
		try
		{
			document = JsonSerializer.Deserialize(json, ShellViewSettingsJsonSerializerContext.Default.ViewSettingsDocument);
		}
		catch (JsonException)
		{
			return null;
		}
		catch (NotSupportedException)
		{
			return null;
		}

		if (document?.SchemaVersion is not CurrentSchemaVersion || document.Settings?.Values is null || document.Settings.Values.DetailsColumns is null)
		{
			return null;
		}

		try
		{
			var values = new BrowseViewSettings(
				(ViewLayoutMode)document.Settings.Values.LayoutMode,
				document.Settings.Values.DetailsColumns.Select(FromDocument),
				document.Settings.Values.SortPropertyId,
				(ViewSortDirection)document.Settings.Values.SortDirection,
				document.Settings.Values.ItemSize,
				document.Settings.Values.GroupPropertyId,
				(ViewSortDirection)document.Settings.Values.GroupDirection);

			return new BrowseViewSettingsOverride((ViewSettingsOverrideFields)document.Settings.Fields, values, (ViewColumnSettingsMode)document.Settings.ColumnMode);
		}
		catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
		{
			return null;
		}
	}

	private static HRESULT CreatePropertyBag(IShellItem shellItem, out IPropertyBag? propertyBag)
	{
		propertyBag = null;
		ITEMIDLIST* absolutePidl = null;
		var hr = PInvoke.SHGetIDListFromObject(shellItem, out absolutePidl);
		if (hr.Failed || absolutePidl is null)
		{
			if (absolutePidl is not null)
			{
				PInvoke.CoTaskMemFree(absolutePidl);
			}

			return hr.Failed ? hr : HRESULT.E_FAIL;
		}

		try
		{
			var propertyBagId = typeof(IPropertyBag).GUID;
			hr = PInvoke.SHGetViewStatePropertyBag(absolutePidl, PropertyBagName, PropertyBagFlags, in propertyBagId, out propertyBag);

			return hr.Failed || propertyBag is not null ? hr : HRESULT.E_NOINTERFACE;
		}
		finally
		{
			PInvoke.CoTaskMemFree(absolutePidl);
		}
	}

	private static ViewColumnSettings FromDocument(ViewColumnDocument? document)
	{
		if (document?.PropertyId is null)
		{
			throw new InvalidDataException("A view settings column does not contain a property ID.");
		}

		return new ViewColumnSettings(document.PropertyId, document.Width, document.Order, document.IsVisible);
	}

	private static ViewSettingsOverrideDocument ToDocument(BrowseViewSettingsOverride settings)
	{
		return new ViewSettingsOverrideDocument
		{
			Fields = (int)settings.Fields,
			ColumnMode = (int)settings.ColumnMode,
			Values = new BrowseViewSettingsDocument
			{
				LayoutMode = (int)settings.Values.LayoutMode,
				DetailsColumns = settings.Values.Columns.Select(ToDocument).ToList(),
				SortPropertyId = settings.Values.SortPropertyId,
				SortDirection = (int)settings.Values.SortDirection,
				ItemSize = settings.Values.ItemSize,
				GroupPropertyId = settings.Values.GroupPropertyId,
				GroupDirection = (int)settings.Values.GroupDirection,
			},
		};
	}

	private static ViewColumnDocument ToDocument(ViewColumnSettings column)
	{
		return new ViewColumnDocument
		{
			PropertyId = column.PropertyId,
			Width = column.Width,
			Order = column.Order,
			IsVisible = column.IsVisible,
		};
	}

	private sealed class ViewSettingsDocument
	{
		public int SchemaVersion { get; set; }

		public ViewSettingsOverrideDocument? Settings { get; set; }
	}

	private sealed class ViewSettingsOverrideDocument
	{
		public int Fields { get; set; }

		public int ColumnMode { get; set; }

		public BrowseViewSettingsDocument? Values { get; set; }
	}

	private sealed class BrowseViewSettingsDocument
	{
		public int LayoutMode { get; set; }

		public List<ViewColumnDocument?>? DetailsColumns { get; set; }

		public string? SortPropertyId { get; set; }

		public int SortDirection { get; set; }

		public double? ItemSize { get; set; }

		public string? GroupPropertyId { get; set; }

		public int GroupDirection { get; set; }
	}

	private sealed class ViewColumnDocument
	{
		public string? PropertyId { get; set; }

		public double Width { get; set; }

		public int Order { get; set; }

		public bool IsVisible { get; set; }
	}

	[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
	[JsonSerializable(typeof(ViewSettingsDocument))]
	private sealed partial class ShellViewSettingsJsonSerializerContext : JsonSerializerContext
	{
	}
}

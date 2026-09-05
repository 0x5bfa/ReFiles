// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Search;
using Windows.Win32.UI.Shell;

namespace Files.Core.Windows;

internal static class WindowsShellSearchFolderFactory
{
	private const ushort LanguageUserDefault = 0x0400;
	private const string SystemIndexCatalog = "SystemIndex";
	private const STRUCTURED_QUERY_RESOLVE_OPTION ResolveOptions = STRUCTURED_QUERY_RESOLVE_OPTION.SQRO_DONT_RESOLVE_DATETIME |
		STRUCTURED_QUERY_RESOLVE_OPTION.SQRO_DONT_MAP_RELATIONS | STRUCTURED_QUERY_RESOLVE_OPTION.SQRO_ADD_ROBUST_ITEM_NAME;

	internal static IShellItem Create(string query, IReadOnlyList<WindowsItemLocator>? scopeLocators)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(query);

		var condition = ParseCondition(query);
		var factory = SearchFolderItemFactory.CreateInstance<ISearchFolderItemFactory>();
		HRESULT hr;
		if (scopeLocators is not null)
		{
			var scope = WindowsShellItemArrayFactory.Create(scopeLocators);
			hr = factory.SetScope(scope);
			hr.ThrowOnFailure();
		}

		hr = factory.SetCondition(condition);
		hr.ThrowOnFailure();
		hr = factory.GetShellItem(out IShellItem shellItem);
		hr.ThrowOnFailure();

		return shellItem;
	}

	private static ICondition ParseCondition(string query)
	{
		var manager = QueryParserManager.CreateInstance<IQueryParserManager>();
		var interfaceId = typeof(IQueryParser).GUID;
		var hr = manager.CreateLoadedParser(SystemIndexCatalog, LanguageUserDefault, in interfaceId, out var parser);
		hr.ThrowOnFailure();
		if (parser is null)
		{
			throw new COMException("The Windows query parser manager returned no parser.", HRESULT.E_NOINTERFACE);
		}

		hr = manager.InitializeOptions(false, true, parser);
		hr.ThrowOnFailure();
		hr = parser.Parse(query, null, out var solution);
		hr.ThrowOnFailure();
		if (solution is null)
		{
			throw new COMException("The Windows query parser returned no solution.", HRESULT.E_FAIL);
		}

		hr = solution.GetQuery(out var condition, out _);
		hr.ThrowOnFailure();
		if (condition is null)
		{
			throw new COMException("The Windows query parser returned no condition.", HRESULT.E_FAIL);
		}

		var factory = solution as IConditionFactory2;
		if (factory is null)
		{
			throw new COMException("The Windows query solution does not support condition resolution.", HRESULT.E_NOINTERFACE);
		}

		hr = factory.ResolveCondition(condition, ResolveOptions, null, out ICondition resolvedCondition);
		hr.ThrowOnFailure();

		return resolvedCondition;
	}
}

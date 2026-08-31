// Copyright (c) Files Community
// Licensed under the MIT License.

#include <iostream>
#include <algorithm>
#include <cwctype>
#include <exdisp.h>
#include <initializer_list>
#include <objbase.h>
#include <propvarutil.h>
#include <shtypes.h>
#include <ShlObj_core.h>
#include <ShObjIdl_core.h>
#include <string_view>
#include <vector>
#include <wil/resource.h>

#include "OpenInFolder.h"

// Link additional libraries
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "Propsys.lib")
#pragma comment(lib, "Advapi32.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "uuid.lib")

constexpr auto ID_TIMEREXPIRED = 101;

LRESULT CALLBACK WindowProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam);
bool OpenInExistingShellWindow(const TCHAR* folderPath);
void RunFileExplorer(const TCHAR* openDirectory);
bool RestoreFileExplorerDefaults();
size_t strifind(const std::wstring& strHaystack, const std::wstring& strNeedle);
bool comparei(std::wstring stringA, std::wstring stringB);
std::string wstring_to_utf8_hex(const std::wstring& input);
std::wstring QuoteCommandLineArgument(std::wstring_view argument);

int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int cmdShow)
{
	auto oleCleanup = wil::OleInitialize_failfast();

	//Uncomment to attach debugger
	//Sleep(10 * 1000);

	int numArgs = 0;
	LPWSTR* arguments = CommandLineToArgvW(GetCommandLineW(), &numArgs);
	std::wstring openDirectory;
	if (arguments != nullptr && numArgs > 1)
		openDirectory.assign(arguments[1]);
	if (arguments != nullptr)
		LocalFree(arguments);

	const bool withArgs = !openDirectory.empty();
	if (withArgs)
		std::wcout << openDirectory << std::endl;

	PWSTR rawLocalAppDataPath = nullptr;
	if (FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData, KF_FLAG_DEFAULT, nullptr, &rawLocalAppDataPath)))
	{
		RunFileExplorer(withArgs ? openDirectory.c_str() : nullptr);

		return 0;
	}

	wil::unique_cotaskmem_string localAppDataPath(rawLocalAppDataPath);
	std::wstring filesExecutablePath(localAppDataPath.get());
	filesExecutablePath.append(L"\\Microsoft\\WindowsApps\\files-dev.exe");
	std::wcout << filesExecutablePath << std::endl;
	if (_waccess(filesExecutablePath.c_str(), 0) == -1)
	{
		std::cout << "Files has been uninstalled" << std::endl;

		MessageBox(
			NULL,
			(LPCWSTR)L"Files has been uninstalled. Restoring File Explorer.",
			(LPCWSTR)L"Files",
			(UINT)(MB_OK)
		);

		if (RestoreFileExplorerDefaults())
			std::cout << "Launcher unset as default" << std::endl;

		RunFileExplorer(withArgs ? openDirectory.c_str() : nullptr);

		return 0;
	}

	if (withArgs)
	{
		if (OpenInExistingShellWindow(openDirectory.c_str()))
		{
			return 0;
		}

		// Register the window class.
		const wchar_t CLASS_NAME[] = L"Files Window Class";

		WNDCLASSEX wcex = { };
		wcex.cbSize = sizeof(wcex);
		wcex.lpfnWndProc = WindowProc;
		wcex.cbWndExtra = sizeof(OpenInFolder*);
		wcex.hInstance = hInstance;
		wcex.lpszClassName = CLASS_NAME;
		RegisterClassEx(&wcex);

		OpenInFolder openInFolder;

		// Create the window.
		HWND hwnd = CreateWindowEx(
			0,
			CLASS_NAME,
			L"Files Launcher",
			0,
			0, 0, 0, 0,
			HWND_MESSAGE,
			NULL,
			hInstance,
			&openInFolder
		);

		if (hwnd == NULL)
			return 0;

		SetTimer(hwnd, ID_TIMEREXPIRED, 500, NULL);

		ShowWindow(hwnd, SW_SHOWNORMAL);
		//UpdateWindow(hwnd);

		MSG msg = { };
		while (GetMessage(&msg, NULL, 0, 0) > 0)
		{
			TranslateMessage(&msg);
			DispatchMessage(&msg);
		}

		auto item = openInFolder.GetResult();

		std::wstring commandLine = QuoteCommandLineArgument(filesExecutablePath);
		if (item.empty())
		{
			std::wcout << L"No item selected" << std::endl;
			commandLine.append(L" -directory ").append(QuoteCommandLineArgument(openDirectory));
		}
		else
		{
			std::wcout << L"Item: " << item << std::endl;
			commandLine.append(L" -select ").append(QuoteCommandLineArgument(item));
		}

		const std::string encodedCommandLine = wstring_to_utf8_hex(commandLine);
		std::wstring uriWithArgs = L"files-dev:?cmd=";
		uriWithArgs.append(encodedCommandLine.begin(), encodedCommandLine.end());

		std::wcout << L"Invoking: " << commandLine << L" = " << uriWithArgs << std::endl;

		SHELLEXECUTEINFO shellExecuteInfo{};
		shellExecuteInfo.cbSize = sizeof(SHELLEXECUTEINFO);
		shellExecuteInfo.fMask = SEE_MASK_NOASYNC | SEE_MASK_FLAG_NO_UI;
		shellExecuteInfo.lpFile = uriWithArgs.c_str();
		shellExecuteInfo.lpDirectory = openDirectory.c_str();
		shellExecuteInfo.nShow = SW_SHOW;

		if (!ShellExecuteEx(&shellExecuteInfo))
		{
			std::wcout << L"Protocol error: " << GetLastError() << std::endl;
		}
	}
	else
	{
		std::wcout << L"Invoking: no arguments" << std::endl;

		SHELLEXECUTEINFO shellExecuteInfo{};
		shellExecuteInfo.cbSize = sizeof(SHELLEXECUTEINFO);
		shellExecuteInfo.fMask = SEE_MASK_NOASYNC | SEE_MASK_FLAG_NO_UI;
		shellExecuteInfo.lpFile = L"files-dev:";
		shellExecuteInfo.nShow = SW_SHOW;

		if (!ShellExecuteEx(&shellExecuteInfo))
		{
			std::wcout << L"Protocol error: " << GetLastError() << std::endl;
		}
	}

	return 0;
}

bool RegistryCommandTargetsFilesLauncher(const wchar_t* subKey)
{
	DWORD valueSize = 0;
	LSTATUS status = RegGetValueW(HKEY_CURRENT_USER, subKey, nullptr, RRF_RT_REG_SZ | RRF_RT_REG_EXPAND_SZ, nullptr, nullptr, &valueSize);
	if (status != ERROR_SUCCESS || valueSize < sizeof(wchar_t))
		return false;

	std::vector<wchar_t> command((valueSize / sizeof(wchar_t)) + 1, L'\0');
	status = RegGetValueW(HKEY_CURRENT_USER, subKey, nullptr, RRF_RT_REG_SZ | RRF_RT_REG_EXPAND_SZ, nullptr, command.data(), &valueSize);
	if (status != ERROR_SUCCESS)
		return false;

	int argumentCount = 0;
	LPWSTR* arguments = CommandLineToArgvW(command.data(), &argumentCount);
	if (arguments == nullptr || argumentCount == 0)
	{
		if (arguments != nullptr)
			LocalFree(arguments);

		return false;
	}

	std::vector<wchar_t> executablePath(MAX_PATH);
	while (true)
	{
		const DWORD pathLength = GetModuleFileNameW(nullptr, executablePath.data(), static_cast<DWORD>(executablePath.size()));
		if (pathLength == 0)
		{
			LocalFree(arguments);

			return false;
		}

		if (pathLength < executablePath.size())
			break;

		executablePath.resize(executablePath.size() * 2);
	}

	const bool targetsCurrentExecutable = CompareStringOrdinal(arguments[0], -1, executablePath.data(), -1, true) == CSTR_EQUAL;
	LocalFree(arguments);

	return targetsCurrentExecutable;
}

bool DeleteRegistryTreeIfOwned(const wchar_t* commandSubKey, std::initializer_list<const wchar_t*> emptyParentSubKeys)
{
	if (!RegistryCommandTargetsFilesLauncher(commandSubKey))
		return false;

	if (RegDeleteTreeW(HKEY_CURRENT_USER, commandSubKey) != ERROR_SUCCESS)
		return false;

	for (const wchar_t* parentSubKey : emptyParentSubKeys)
		RegDeleteKeyW(HKEY_CURRENT_USER, parentSubKey);

	return true;
}

bool RestoreFileExplorerDefaults()
{
	bool changed = false;
	changed |= DeleteRegistryTreeIfOwned(L"Software\\Classes\\Folder\\shell\\open\\command", { L"Software\\Classes\\Folder\\shell\\open" });
	changed |= DeleteRegistryTreeIfOwned(L"Software\\Classes\\Folder\\shell\\explore\\command", { L"Software\\Classes\\Folder\\shell\\explore" });
	changed |= DeleteRegistryTreeIfOwned(
		L"Software\\Classes\\CLSID\\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}\\shell\\opennewwindow\\command",
		{
			L"Software\\Classes\\CLSID\\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}\\shell\\opennewwindow",
			L"Software\\Classes\\CLSID\\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}\\shell",
			L"Software\\Classes\\CLSID\\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}",
		});

	if (changed)
		SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, nullptr, nullptr);

	return changed;
}

LRESULT CALLBACK WindowProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam)
{
	auto* pContainer = (OpenInFolder*)GetWindowLongPtr(hwnd, GWLP_USERDATA);

	switch (uMsg)
	{
	case WM_NCCREATE:
	{
		CREATESTRUCT* pCreate = reinterpret_cast<CREATESTRUCT*>(lParam);
		pContainer = reinterpret_cast<OpenInFolder*>(pCreate->lpCreateParams);

		if (!pContainer)
		{
			PostQuitMessage(0);
			return 0;
		}

		pContainer->SetWindow(hwnd);
		SetWindowLongPtr(hwnd, GWLP_USERDATA, (LONG_PTR)pContainer);
		break;
	}

	case WM_NCDESTROY:
		SetWindowLongPtr(hwnd, GWLP_USERDATA, 0);
		return 0;

	case WM_TIMER:
		switch (wParam)
		{
		case ID_TIMEREXPIRED:
			PostQuitMessage(0);
			return 0;
		}
		break;
	}

	 // Jump across to the member window function (will handle all requests).
	if (pContainer != nullptr)
		return pContainer->WindowProcedure(hwnd, uMsg, wParam, lParam);
	else
		return DefWindowProc(hwnd, uMsg, wParam, lParam);
}

size_t strifind(const std::wstring& strHaystack, const std::wstring& strNeedle)
{
	auto it = std::search(
		strHaystack.begin(), strHaystack.end(),
		strNeedle.begin(), strNeedle.end(),
		[](wchar_t ch1, wchar_t ch2) { return std::towupper(ch1) == std::towupper(ch2); }
	);

	return it != strHaystack.end() ? it - strHaystack.begin() : std::wstring::npos;
}

bool comparei(std::wstring stringA, std::wstring stringB)
{
	auto toUpperW = [](wchar_t c) { return static_cast<wchar_t>(std::towupper(c)); };
	transform(stringA.begin(), stringA.end(), stringA.begin(), toUpperW);
	transform(stringB.begin(), stringB.end(), stringB.begin(), toUpperW);

	return (stringA == stringB);
}

std::string wstring_to_utf8_hex(const std::wstring& input)
{
	std::string output;

	const int cbNeeded = WideCharToMultiByte(CP_UTF8, 0, input.c_str(), static_cast<int>(input.size()), nullptr, 0, nullptr, nullptr);
	if (cbNeeded <= 0)
		return output;

	std::vector<char> utf8(cbNeeded);
	if (WideCharToMultiByte(CP_UTF8, 0, input.c_str(), static_cast<int>(input.size()), utf8.data(), cbNeeded, nullptr, nullptr) == 0)
		return output;

	output.reserve(utf8.size() * 3);
	for (const unsigned char value : utf8)
	{
		char onehex[4];
		sprintf_s(onehex, sizeof(onehex), "%%%02X", value);
		output.append(onehex);
	}

	return output;
}

std::wstring QuoteCommandLineArgument(std::wstring_view argument)
{
	std::wstring quoted(1, L'"');
	size_t backslashCount = 0;
	for (const wchar_t character : argument)
	{
		if (character == L'\\')
		{
			backslashCount++;
			continue;
		}

		if (character == L'"')
		{
			quoted.append((backslashCount * 2) + 1, L'\\');
			quoted.push_back(character);
			backslashCount = 0;
			continue;
		}

		quoted.append(backslashCount, L'\\');
		backslashCount = 0;
		quoted.push_back(character);
	}

	quoted.append(backslashCount * 2, L'\\');
	quoted.push_back(L'"');

	return quoted;
}

void RunFileExplorer(const TCHAR* openDirectory)
{
	SHELLEXECUTEINFO shellExecuteInfo{};
	shellExecuteInfo.cbSize = sizeof(SHELLEXECUTEINFO);
	shellExecuteInfo.lpFile = L"explorer.exe";
	std::wstring parameters;

	if (openDirectory != nullptr)
	{
		parameters = QuoteCommandLineArgument(openDirectory);
		shellExecuteInfo.lpParameters = parameters.c_str();
	}

	shellExecuteInfo.nShow = SW_SHOW;
	ShellExecuteEx(&shellExecuteInfo);
}

bool OpenInExistingShellWindow(const TCHAR* folderPath)
{
	std::wstring openDirectory(folderPath);
	bool mustOpenInExplorer = false;
	constexpr auto godModeClsid = L"{ED7BA470-8E54-465E-825C-99712043E01C}";

	if (strifind(openDirectory, L"::{") == 0)
		openDirectory = L"shell:" + openDirectory;

	// Exclude this shell address so that it opens in File Explorer
	if (strifind(openDirectory, godModeClsid) != std::wstring::npos)
		mustOpenInExplorer = true;

	if (strifind(openDirectory, L"shell:") == 0)
	{
		std::vector<std::wstring> supportedShellFolders{
			L"shell:::{645FF040-5081-101B-9F08-00AA002F954E}",
			L"shell:::{5E5F29CE-E0A8-49D3-AF32-7A7BDC173478}",
			L"shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
			L"shell:::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}",
			L"shell:::{208D2C60-3AEA-1069-A2D7-08002B30309D}",
			L"Shell:RecycleBinFolder", L"Shell:NetworkPlacesFolder", L"Shell:MyComputerFolder"
		};

		auto it = std::find_if(
			supportedShellFolders.begin(), supportedShellFolders.end(),
			[openDirectory](std::wstring it) { return comparei(it, openDirectory); }
		);

		mustOpenInExplorer = it == supportedShellFolders.end();
	}

	auto createPidl = [](const wchar_t* parsingName, wil::unique_cotaskmem_ptr<ITEMIDLIST_ABSOLUTE>& pidl) -> HRESULT
	{
		winrt::com_ptr<IShellItem> shellItem;
		RETURN_IF_FAILED(SHCreateItemFromParsingName(parsingName, nullptr, IID_PPV_ARGS(shellItem.put())));

		PIDLIST_ABSOLUTE rawPidl = nullptr;
		RETURN_IF_FAILED(SHGetIDListFromObject(shellItem.get(), &rawPidl));
		pidl.reset(rawPidl);

		return S_OK;
	};

	wil::unique_cotaskmem_ptr<ITEMIDLIST_ABSOLUTE> controlPanelCategoryViewPidl;
	if (FAILED(createPidl(L"::{26EE0668-A00A-44D7-9371-BEB064C98683}", controlPanelCategoryViewPidl)))
	{
		if (mustOpenInExplorer)
			RunFileExplorer(openDirectory.c_str());

		return mustOpenInExplorer;
	}

	wil::unique_cotaskmem_ptr<ITEMIDLIST_ABSOLUTE> targetFolderPidl;
	if (FAILED(createPidl(openDirectory.c_str(), targetFolderPidl)))
	{
		if (mustOpenInExplorer)
			RunFileExplorer(openDirectory.c_str());

		return mustOpenInExplorer;
	}

	bool opened = false;
	winrt::com_ptr<IShellWindows> shellWindows;
	if (SUCCEEDED(CoCreateInstance(CLSID_ShellWindows, nullptr, CLSCTX_LOCAL_SERVER, IID_PPV_ARGS(shellWindows.put()))))
	{
		VARIANT index{};
		V_VT(&index) = VT_I4;
		for (V_I4(&index) = 0; ; V_I4(&index)++)
		{
			winrt::com_ptr<IDispatch> item;
			if (FAILED(shellWindows->Item(index, item.put())) || item == nullptr)
				break;

			winrt::com_ptr<IServiceProvider> serviceProvider;
			if (FAILED(item->QueryInterface(IID_PPV_ARGS(serviceProvider.put()))))
				continue;

			winrt::com_ptr<IShellBrowser> shellBrowser;
			if (FAILED(serviceProvider->QueryService(SID_STopLevelBrowser, IID_PPV_ARGS(shellBrowser.put()))))
				continue;

			winrt::com_ptr<IShellView> shellView;
			if (FAILED(shellBrowser->QueryActiveShellView(shellView.put())))
				continue;

			winrt::com_ptr<IFolderView> folderView;
			if (FAILED(shellView->QueryInterface(IID_PPV_ARGS(folderView.put()))))
				continue;

			winrt::com_ptr<IPersistFolder2> folder;
			if (FAILED(folderView->GetFolder(IID_PPV_ARGS(folder.put()))))
				continue;

			PIDLIST_ABSOLUTE rawFolderPidl = nullptr;
			if (FAILED(folder->GetCurFolder(&rawFolderPidl)))
				continue;
			wil::unique_cotaskmem_ptr<ITEMIDLIST_ABSOLUTE> folderPidl(rawFolderPidl);

			if (!ILIsParent(folderPidl.get(), targetFolderPidl.get(), true) && !ILIsEqual(folderPidl.get(), controlPanelCategoryViewPidl.get()))
				continue;

			if (SUCCEEDED(shellBrowser->BrowseObject(targetFolderPidl.get(), SBSP_SAMEBROWSER | SBSP_ABSOLUTE)))
			{
				opened = true;

				break;
			}
		}
	}

	if (!opened && mustOpenInExplorer)
		RunFileExplorer(openDirectory.c_str());

	return opened || mustOpenInExplorer;
}

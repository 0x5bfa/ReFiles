// Copyright (c) Files Community
// Licensed under the MIT License.

#include "OpenInFolder.h"

OpenInFolder::OpenInFolder()
{
	m_shellWindows = winrt::create_instance<IShellWindows>(CLSID_ShellWindows, CLSCTX_ALL);
}

void OpenInFolder::SetWindow(HWND hwnd)
{
	m_hwnd = hwnd;
}

void OpenInFolder::OnCreate()
{
	int numArgs = 0;
	LPWSTR* arguments = CommandLineToArgvW(GetCommandLineW(), &numArgs);

	if (arguments == nullptr || numArgs < 2)
	{
		if (arguments != nullptr)
			LocalFree(arguments);

		return;
	}

	std::wstring openDirectory(arguments[1]);
	LocalFree(arguments);

	if (openDirectory.empty())
		return;

	winrt::com_ptr<IShellItem> item;
	if (FAILED(SHCreateItemFromParsingName(openDirectory.c_str(), nullptr, IID_PPV_ARGS(item.put()))))
		return;

	PIDLIST_ABSOLUTE rawDirectoryPidl = nullptr;
	if (FAILED(SHGetIDListFromObject(item.get(), &rawDirectoryPidl)))
		return;
	wil::unique_cotaskmem_ptr<ITEMIDLIST_ABSOLUTE> directoryPidl(rawDirectoryPidl);

	if (FAILED(NotifyShellOfNavigation(directoryPidl.get())))
		return;
}

LRESULT CALLBACK OpenInFolder::WindowProcedure(HWND hwnd, UINT Msg, WPARAM wParam, LPARAM lParam)
{
	switch (Msg)
	{
	case WM_CREATE:
		OnCreate();
		break;

	case WM_CLOSE:
		DestroyWindow(hwnd);
		break;

	case WM_DESTROY:
		PostQuitMessage(0);
		break;
	}

	return DefWindowProc(hwnd, Msg, wParam, lParam);
}

HRESULT OpenInFolder::NotifyShellOfNavigation(PCIDLIST_ABSOLUTE pidl)
{
	wil::unique_variant pidlVariant;
	RETURN_IF_FAILED(InitVariantFromBuffer(pidl, ILGetSize(pidl), &pidlVariant));

	wil::unique_variant empty;
	long shellWindowCookie = 0;
	RETURN_IF_FAILED(m_shellWindows->RegisterPending(GetCurrentThreadId(), &pidlVariant, &empty, SWC_BROWSER, &shellWindowCookie));

	m_shellWindowCookie = shellWindowCookie;
	m_shellWindowRegistered = true;
	m_shellWindows->OnNavigate(shellWindowCookie, &pidlVariant);
	//m_shellWindows->OnActivated(m_shellWindowCookie, VARIANT_TRUE);

	return S_OK;
}

void OpenInFolder::OnItemSelected(PIDLIST_ABSOLUTE pidl)
{
	winrt::com_ptr<IShellItem> item;
	if (FAILED(SHCreateItemFromIDList(pidl, IID_PPV_ARGS(item.put()))))
		return;

	PWSTR rawPath = nullptr;
	if (FAILED(item->GetDisplayName(SIGDN_DESKTOPABSOLUTEPARSING, &rawPath)))
		return;

	wil::unique_cotaskmem_string path(rawPath);
	m_selectedItem = path.get();
	PostMessage(m_hwnd, WM_CLOSE, 0, 0);
}

std::wstring OpenInFolder::GetResult()
{
	return m_selectedItem;
}

OpenInFolder::~OpenInFolder()
{
	if (m_shellWindows && m_shellWindowRegistered)
		m_shellWindows->Revoke(m_shellWindowCookie);
}

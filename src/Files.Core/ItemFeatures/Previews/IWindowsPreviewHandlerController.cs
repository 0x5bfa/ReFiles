// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Previews;

public interface IWindowsPreviewHandlerController : IDisposable
{
	void SetSite();

	bool TryInitializeWithStream(string fileSystemPath);

	bool TryInitializeWithItem(string parsingName);

	bool TryInitializeWithFile(string fileSystemPath);

	void SetWindow(nint windowHandle, WindowsPreviewBounds bounds);

	void SetBounds(WindowsPreviewBounds bounds);

	void SetTheme(WindowsPreviewColor background, WindowsPreviewColor foreground);

	void DoPreview();

	void SetFocus();

	nint QueryFocus();

	bool TryTranslateAccelerator(nint messagePointer);
}

public interface IWindowsPreviewHandlerControllerFactory
{
	IWindowsPreviewHandlerController Create(Guid handlerClsid);
}

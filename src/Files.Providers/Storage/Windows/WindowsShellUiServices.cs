// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Drawing;

namespace Files.Core.Storage.Windows;

/// <summary>Adapts a configured Windows source to borrowed consumer UI services.</summary>
public sealed class WindowsShellUiServices : IWindowsShellUiServices
{
	private readonly WindowsStorageSource _source;
	/// <inheritdoc />
	public StorageSourceId SourceId => _source.SourceId;
	/// <inheritdoc />
	public IWindowsShellClipboardService Clipboard => _source.Clipboard;
	/// <inheritdoc />
	public IWindowsShellDragDropService DragDrop => _source.DragDrop;
	/// <inheritdoc />
	public IWindowsShellPropertiesService Properties { get; }
	/// <inheritdoc />
	public IWindowsShellContextualCommandService ContextualCommands { get; }
	/// <inheritdoc />
	public IWindowsShellDefaultCommandInvoker DefaultCommands { get; }
	/// <inheritdoc />
	public IWindowsShellPreviewSessionFactory? Previews { get; }

	/// <summary>Creates the borrowed services for an existing source.</summary>
	/// <param name="source">The runtime-owned source.</param>
	/// <param name="previews">The runtime-owned preview factory.</param>
	public WindowsShellUiServices(WindowsStorageSource source, IWindowsShellPreviewSessionFactory? previews)
	{
		ArgumentNullException.ThrowIfNull(source);

		_source = source;
		Properties = new WindowsShellPropertiesService(source);
		ContextualCommands = new WindowsShellContextualCommandService(source);
		DefaultCommands = new WindowsShellDefaultCommandInvoker(source);
		Previews = previews;
	}

	/// <inheritdoc />
#pragma warning disable RS0030 // WinRT provides the registered synchronization roots for the Windows adapter.
	public IReadOnlyList<string> GetSyncRootPaths()
	{
		return global::Windows.Storage.Provider.StorageProviderSyncRootManager.GetCurrentSyncRoots()
			.Select(static root => root.Path?.Path)
			.Where(static path => !string.IsNullOrWhiteSpace(path))
			.Select(static path => System.IO.Path.TrimEndingDirectorySeparator(path!)).ToArray();
	}
#pragma warning restore RS0030

	/// <inheritdoc />
	public ValueTask<bool> IsShellDesktopAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		return _source.IsShellDesktopAsync(reference, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IWindowsShellContextMenuSession?> CreateContextMenuAsync(IReadOnlyList<StorableReference> selection, WindowsShellInvocationContext context,
		IReadOnlyList<WindowsShellContextMenuEntry> applicationEntries, WindowsShellContextMenuEntry? expandedMenuEntry, bool extendedVerbs, CancellationToken cancellationToken = default)
	{
		return await WindowsShellContextMenuSession.CreateAsync(_source, selection, context, applicationEntries, expandedMenuEntry, extendedVerbs, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<IWindowsShellContextMenuTarget?> GetContextMenuTargetAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken = default)
	{
		return await WindowsShellContextMenuTarget.CreateAsync(_source, selection, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public bool ShowContextMenu(nint owner, IWindowsShellContextMenuTarget target, Point point)
	{
		if (target is not WindowsShellContextMenuTarget nativeTarget)
		{
			throw new ArgumentException("The menu target was not captured by the Windows menu service.", nameof(target));
		}

		return new WindowsShellClassicContextMenuSession().Show(owner, nativeTarget, point);
	}

}

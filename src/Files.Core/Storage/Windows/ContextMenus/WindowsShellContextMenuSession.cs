// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.Core.Storage.Windows;

/// <summary>Owns the native commands, final menu entries, and asynchronous work for one selection popup.</summary>
/// <remarks>Native state is accessed exclusively through the storage source's ordered operation STA. Dispose the session before its storage source.</remarks>
public sealed class WindowsShellContextMenuSession : IAsyncDisposable
{
	private readonly Lock _lifetimeLock = new();
	private readonly WindowsStorageSource _source;
	private readonly WindowsShellInvocationContext? _invocationContext;
	private readonly Dictionary<WindowsShellContextMenuEntry, uint> _nativeCommands = [];
	private readonly Dictionary<WindowsShellContextMenuEntry, IExplorerCommand> _explorerCommands = [];
	private readonly Dictionary<WindowsShellContextMenuEntry, HBITMAP> _nativeIcons = [];
	private readonly Dictionary<(WindowsShellContextMenuEntry Entry, int Size, WindowsShellContextMenuIconTheme Theme), Task<ReadOnlyMemory<byte>>> _iconLoads = [];
	private readonly HashSet<WindowsShellContextMenuEntry> _entries = [];
	private IContextMenu? _contextMenu;
	private IShellItemArray? _selection;
	private DestroyMenuSafeHandle? _menu;
	private WindowsShellContextMenuSite? _site;
	private Task? _disposeTask;
	private bool _isDisposed;

	/// <summary>Gets the completed menu hierarchy, including the application contributions supplied during creation.</summary>
	public IReadOnlyList<WindowsShellContextMenuEntry> Entries { get; private set; } = [];

	private WindowsShellContextMenuSession(WindowsStorageSource source, WindowsShellInvocationContext? invocationContext)
	{
		_source = source;
		_invocationContext = invocationContext;
	}

	/// <summary>Creates the native command source and final menu model for one selection.</summary>
	/// <param name="source">The Windows source whose STA owns the native state.</param>
	/// <param name="selection">The immutable selected item references.</param>
	/// <param name="invocationContext">The optional window and input context for Shell commands.</param>
	/// <param name="applicationEntries">Application commands placed before the Shell sections.</param>
	/// <param name="expandedMenuEntry">The optional application entry that opens the expanded native menu.</param>
	/// <param name="extendedVerbs">Whether Shift was pressed when the menu was requested.</param>
	/// <param name="cancellationToken">Cancels selection resolution and construction.</param>
	/// <returns>The owned session, or null when the selection cannot expose one Shell menu.</returns>
	public static async Task<WindowsShellContextMenuSession?> CreateAsync(WindowsStorageSource source, IReadOnlyList<Storage.StorableReference> selection,
		WindowsShellInvocationContext? invocationContext = null, IReadOnlyList<WindowsShellContextMenuEntry>? applicationEntries = null,
		WindowsShellContextMenuEntry? expandedMenuEntry = null, bool extendedVerbs = false, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);

		ArgumentNullException.ThrowIfNull(selection);

		var resolved = await WindowsShellMenuSelectionResolver.ResolveAsync(source, selection.ToArray(), cancellationToken).ConfigureAwait(false);
		if (resolved is null || !WindowsShellMenuSelectionResolver.HasCommonParent(resolved.Locators))
		{
			return null;
		}

		var appEntries = applicationEntries?.ToArray() ?? [];
		var session = new WindowsShellContextMenuSession(source, invocationContext);
		try
		{
			await source.Scheduler.InvokeOperationAsync(() =>
			{
				session.Initialize(resolved, appEntries, expandedMenuEntry, extendedVerbs, cancellationToken);

				return true;
			}, cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			await session.DisposeAsync().ConfigureAwait(false);
			throw;
		}

		return session;
	}

	/// <summary>Invokes the original Shell command belonging to an entry in this session.</summary>
	/// <param name="entry">An enabled Shell command returned by this session.</param>
	/// <param name="cancellationToken">Cancels the invocation before it starts.</param>
	/// <returns>Whether the original command was invoked.</returns>
	public Task<bool> InvokeAsync(WindowsShellContextMenuEntry entry, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entry);

		lock (_lifetimeLock)
		{
			ObjectDisposedException.ThrowIf(_isDisposed, this);

			return _source.Scheduler.InvokeOperationAsync(() => InvokeOnSta(entry), cancellationToken);
		}
	}

	/// <summary>Loads an entry's icon without changing the menu hierarchy.</summary>
	/// <param name="entry">An entry returned by this session.</param>
	/// <param name="pixelSize">The requested square size in physical pixels.</param>
	/// <param name="cancellationToken">Cancels this caller's wait for the shared icon load.</param>
	/// <returns>Encoded PNG, SVG, or image data, or an empty value when no icon is available.</returns>
	public Task<ReadOnlyMemory<byte>> GetIconAsync(WindowsShellContextMenuEntry entry, int pixelSize, CancellationToken cancellationToken = default)
	{
		return GetIconAsync(entry, pixelSize, WindowsShellContextMenuIconTheme.Light, cancellationToken);
	}

	/// <summary>Loads an entry's icon using the requested Windows theme assets.</summary>
	/// <param name="entry">An entry returned by this session.</param>
	/// <param name="pixelSize">The requested square size in physical pixels.</param>
	/// <param name="theme">The contrast and background theme used by the menu.</param>
	/// <param name="cancellationToken">Cancels this caller's wait for the shared icon load.</param>
	/// <returns>Encoded PNG, SVG, or image data, or an empty value when no icon is available.</returns>
	public Task<ReadOnlyMemory<byte>> GetIconAsync(WindowsShellContextMenuEntry entry, int pixelSize, WindowsShellContextMenuIconTheme theme, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entry);

		ArgumentOutOfRangeException.ThrowIfLessThan(pixelSize, 1);

		ArgumentOutOfRangeException.ThrowIfGreaterThan(pixelSize, 256);

		lock (_lifetimeLock)
		{
			ObjectDisposedException.ThrowIf(_isDisposed, this);

			if (!_entries.Contains(entry))
			{
				throw new ArgumentException("The menu entry belongs to another session.", nameof(entry));
			}

			if (!_iconLoads.TryGetValue((entry, pixelSize, theme), out var load))
			{
				load = _source.Scheduler.InvokeOperationAsync(() => ReadIconOnSta(entry, pixelSize, theme));
				_iconLoads.Add((entry, pixelSize, theme), load);
			}

			return load.WaitAsync(cancellationToken);
		}
	}

	/// <summary>Stops accepting work and releases native menu state after previously queued operations finish.</summary>
	/// <returns>The asynchronous completion of session teardown.</returns>
	public ValueTask DisposeAsync()
	{
		lock (_lifetimeLock)
		{
			if (_disposeTask is null)
			{
				_isDisposed = true;
				_disposeTask = _source.Scheduler.InvokeOperationAsync(() =>
				{
					DetachSite(_contextMenu);
					foreach (var command in _explorerCommands.Values.Distinct())
					{
						DetachSite(command);
					}

					_nativeCommands.Clear();
					_explorerCommands.Clear();
					_nativeIcons.Clear();
					_iconLoads.Clear();
					_entries.Clear();
					_menu?.Dispose();
					_menu = null;
					_contextMenu = null;
					_selection = null;
					_site = null;

					return true;
				});
			}

			return new(_disposeTask);
		}
	}

	private void Initialize(WindowsShellResolvedSelection resolved, IReadOnlyList<WindowsShellContextMenuEntry> applicationEntries,
		WindowsShellContextMenuEntry? expandedMenuEntry, bool extendedVerbs, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		_selection = WindowsShellItemArrayFactory.Create(resolved.Locators);
		_site = _invocationContext is null ? null : new WindowsShellContextMenuSite((HWND)_invocationContext.OwnerWindowHandle);
		_contextMenu = WindowsShellContextMenuCommandHelper.Create(_selection);
		if (_site is not null && _contextMenu is IObjectWithSite objectWithSite)
		{
			objectWithSite.SetSite(_site).ThrowOnFailure();
		}

		_menu = WindowsShellContextMenuCommandHelper.CreateMenu();
		const uint ItemMenu = 0x80;
		const uint ExtendedVerbs = 0x100;
		_contextMenu.QueryContextMenu(_menu, 0, WindowsShellContextMenuCommandHelper.FirstCommandId,
			WindowsShellContextMenuCommandHelper.LastCommandId, ItemMenu | (extendedVerbs ? ExtendedVerbs : 0)).ThrowOnFailure();
		var cloudVerbs = new List<WindowsShellContextMenuEntry>();
		foreach (var native in WindowsShellNativeMenuReader.ReadCloudVerbs(_menu, _contextMenu))
		{
			var entry = new WindowsShellContextMenuEntry(native.Verb, native.Title, native.IsEnabled, native.IsChecked, native.IsRadio, hasNativeIcon: true);
			_nativeCommands.Add(entry, native.Ordinal);
			if (!native.Bitmap.IsNull)
			{
				_nativeIcons.Add(entry, native.Bitmap);
			}

			cloudVerbs.Add(entry);
		}

		var extensions = WindowsFileExplorerAppExtensionCatalog.GetExtensions(resolved.ItemTypes);
		var provider = resolved.FileSystemPaths.Count == resolved.Locators.Count ? WindowsShellCloudProviderSource.GetProviderForSelection(resolved.FileSystemPaths) : null;
		var groups = ReadExtensionGroups(extensions, provider, cancellationToken);
		Entries = WindowsShellContextMenuBuilder.Build(applicationEntries, cloudVerbs, groups, expandedMenuEntry);
		RegisterEntries(Entries);
		cancellationToken.ThrowIfCancellationRequested();

	}

	private IReadOnlyList<WindowsShellContextMenuGroup> ReadExtensionGroups(IReadOnlyList<WindowsFileExplorerAppExtensionGroup> extensions,
		WindowsShellCloudProviderInfo? provider, CancellationToken cancellationToken)
	{
		var groups = new List<WindowsShellContextMenuGroup>();
		var providerAdded = false;
		foreach (var extension in extensions)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var isProvider = provider is not null && !string.IsNullOrWhiteSpace(provider.PackageFullName)
				&& string.Equals(extension.PackageFullName, provider.PackageFullName, StringComparison.OrdinalIgnoreCase);
			var registrations = extension.Registrations.ToList();
			if (isProvider)
			{
				providerAdded = true;
				AppendProviderRegistrations(registrations, provider!);
			}

			var entries = ReadExplorerCommands(registrations, cancellationToken);
			var icon = CreateIcon(isProvider ? provider!.LogoPath ?? extension.LogoPath : extension.LogoPath, extension.PackageFullName,
				isProvider ? provider!.IconPath : null, isProvider ? provider!.IconIndex : 0);
			groups.Add(new(extension.PackageFullName, isProvider ? provider!.DisplayName ?? extension.DisplayName : extension.DisplayName, isProvider, entries, icon));
		}

		if (provider is not null && !providerAdded && !string.IsNullOrWhiteSpace(provider.PackageFullName))
		{
			var registrations = new List<WindowsFileExplorerAppExtensionRegistration>();
			AppendProviderRegistrations(registrations, provider);
			groups.Add(new(provider.PackageFullName, provider.DisplayName ?? string.Empty, true, ReadExplorerCommands(registrations, cancellationToken),
				CreateIcon(provider.LogoPath, provider.PackageFullName, provider.IconPath, provider.IconIndex)));
		}

		return groups;
	}

	private IReadOnlyList<WindowsShellContextMenuEntry> ReadExplorerCommands(IReadOnlyList<WindowsFileExplorerAppExtensionRegistration> registrations, CancellationToken cancellationToken)
	{
		var entries = new List<WindowsShellContextMenuEntry>();
		foreach (var registration in registrations)
		{
			cancellationToken.ThrowIfCancellationRequested();

			try
			{
				if (WindowsShellExplorerCommandSource.TryCreateExplorerCommand(registration, _site) is not { } command)
				{
					continue;
				}

				var retained = new Dictionary<WindowsShellExplorerCommand, IExplorerCommand>();
				var description = WindowsShellExplorerCommandSource.TryCreateDescription(command, _selection!, new(registration.ClassId, registration.VerbId, []),
					registration.DisplayName, 0, cancellationToken, registration.StaticDisplayName, registration.StaticIconPath, registration.PackageFullName,
					retain: (entry, backend) => retained.Add(entry, backend));
				if (description is not null)
				{
					entries.Add(ConvertExplorerCommand(description, retained));
				}
			}
			catch (COMException)
			{
				// One unavailable extension must not discard the other commands.
			}
		}

		return entries;
	}

	private WindowsShellContextMenuEntry ConvertExplorerCommand(WindowsShellExplorerCommand command, IReadOnlyDictionary<WindowsShellExplorerCommand, IExplorerCommand> retained)
	{
		var icon = CreateIcon(command.IconPath, command.IconPackageFullName, command.FallbackIconPath, command.FallbackIconIndex, command.IconIndex);
		var children = command.Children.Select(child => ConvertExplorerCommand(child, retained)).ToArray();
		var entry = new WindowsShellContextMenuEntry(command.Id, command.Title, command.IsEnabled, command.IsChecked, command.IsRadio, command.IsSeparator, children, icon);
		_explorerCommands.Add(entry, retained[command]);

		return entry;
	}

	private bool InvokeOnSta(WindowsShellContextMenuEntry entry)
	{
		if (!entry.IsEnabled || entry.IsApplicationCommand || entry.IsSeparator || entry.Children.Count is not 0)
		{
			return false;
		}

		if (_nativeCommands.TryGetValue(entry, out var ordinal) && _contextMenu is not null)
		{
			WindowsShellContextMenuCommandHelper.Invoke(_contextMenu, ordinal, _invocationContext);

			return true;
		}

		if (!_explorerCommands.TryGetValue(entry, out var command) || _selection is null)
		{
			return false;
		}

		var hr = command.Invoke(_selection, null!);
		if (hr != HRESULT.E_NOTIMPL)
		{
			return hr.Succeeded;
		}

		if (command is not IObjectWithSelection objectWithSelection || command is not IExecuteCommand executeCommand || objectWithSelection.SetSelection(_selection).Failed)
		{
			return false;
		}

		if (_invocationContext?.WorkingDirectory is { } workingDirectory)
		{
			executeCommand.SetDirectory(workingDirectory);
		}

		if (_invocationContext?.InvocationPoint is { } invocationPoint)
		{
			executeCommand.SetPosition(new System.Drawing.Point(invocationPoint.X, invocationPoint.Y));
		}

		executeCommand.SetShowWindow((int)SHOW_WINDOW_CMD.SW_SHOWNORMAL);

		return executeCommand.Execute().Succeeded;
	}

	private ReadOnlyMemory<byte> ReadIconOnSta(WindowsShellContextMenuEntry entry, int pixelSize, WindowsShellContextMenuIconTheme theme)
	{
		if (_nativeCommands.ContainsKey(entry))
		{
			var asset = WindowsShellContextMenuAssets.Read(entry.Id, theme);
			if (!asset.IsEmpty)
			{
				return asset;
			}
		}

		if (_nativeIcons.TryGetValue(entry, out var bitmap))
		{
			return WindowsThumbnailRenderer.EncodeHBitmap(bitmap, CancellationToken.None) ?? [];
		}

		if (entry.Icon is not { } icon)
		{
			return _nativeCommands.ContainsKey(entry) && _selection is not null
				? WindowsShellCommandStoreIconSource.Read(entry.Id, _selection, _site, pixelSize) : ReadOnlyMemory<byte>.Empty;
		}

		return WindowsShellIconProvider.GetAppExtensionIcon(icon.Path, icon.Index, pixelSize, CancellationToken.None,
			icon.PackageFullName, icon.FallbackPath, icon.FallbackIndex, icon.FallbackPackageFullName);
	}

	private void RegisterEntries(IReadOnlyList<WindowsShellContextMenuEntry> entries)
	{
		foreach (var entry in entries)
		{
			_entries.Add(entry);
			RegisterEntries(entry.Children);
		}
	}

	private static void AppendProviderRegistrations(List<WindowsFileExplorerAppExtensionRegistration> registrations, WindowsShellCloudProviderInfo provider)
	{
		foreach (var classId in provider.CloudFileVerbClassIds)
		{
			if (!registrations.Any(registration => registration.ClassId == classId))
			{
				registrations.Add(new(classId, classId.ToString("B"), provider.DisplayName ?? string.Empty, provider.PackageFullName, IsCloudProviderVerb: true));
			}
		}
	}

	private static WindowsShellContextMenuIcon? CreateIcon(string? location, string? packageFullName, string? fallbackPath = null, int fallbackIndex = 0, int? resourceIndex = null)
	{
		var (path, index) = WindowsShellExplorerCommandSource.ParseCommandIcon(location);
		if (path is null)
		{
			return string.IsNullOrWhiteSpace(fallbackPath) ? null : new(fallbackPath, fallbackIndex, packageFullName);
		}

		return new(path, resourceIndex ?? index, packageFullName, fallbackPath, fallbackIndex, packageFullName);
	}

	private static void DetachSite(object? command)
	{
		if (command is IObjectWithSite objectWithSite)
		{
			objectWithSite.SetSite(null!);
		}
	}
}

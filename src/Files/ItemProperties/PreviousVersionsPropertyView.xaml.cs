// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Files.Adapters;
using Files.Core.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Files.ItemProperties;

public sealed partial class PreviousVersionsPropertyView : UserControl
{
	private readonly Action<string, string?> _launchSystemTool;

	internal ObservableCollection<PreviousVersionViewModel> Versions { get; }

	internal Visibility EmptyVisibility => Versions.Count is 0 ? Visibility.Visible : Visibility.Collapsed;

	internal PreviousVersionsPropertyView(IReadOnlyList<WindowsShellPreviousVersion> versions, Action<string, string?> launchSystemTool)
	{
		Versions = new(versions.Select(static version => new PreviousVersionViewModel(version)));
		_launchSystemTool = launchSystemTool;
		InitializeComponent();
		_ = LoadFolderIconsAsync();
	}

	private async Task LoadFolderIconsAsync()
	{
		var iconData = WindowsShellIconProvider.GetFolderIcon();
		if (iconData.IsEmpty)
		{
			return;
		}

		var icon = await ThumbnailImageFactory.CreateAsync(iconData);
		foreach (var version in Versions)
		{
			version.Icon = icon;
		}
	}

	private void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		OpenButton.IsEnabled = VersionList.SelectedItem is PreviousVersionViewModel;
	}

	private void OpenButton_Click(object sender, RoutedEventArgs e)
	{
		if (VersionList.SelectedItem is PreviousVersionViewModel version)
		{
			_launchSystemTool(version.Version.SourcePath, null);
		}
	}
}

internal sealed class PreviousVersionViewModel : INotifyPropertyChanged
{
	private BitmapImage? _icon;

	public DateTimeOffset DateModified => Version.DateModified;

	public BitmapImage? Icon
	{
		get => _icon;
		internal set
		{
			if (!ReferenceEquals(_icon, value))
			{
				_icon = value;
				OnPropertyChanged();
			}
		}
	}

	public string Name => Version.Name;

	internal WindowsShellPreviousVersion Version { get; }

	public event PropertyChangedEventHandler? PropertyChanged;

	internal PreviousVersionViewModel(WindowsShellPreviousVersion version)
	{
		Version = version;
	}

	private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}

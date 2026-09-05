// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Files.Adapters;
using Files.Localization;
using Files.Core.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Win32.Foundation;

namespace Files.ItemProperties;

public sealed partial class SecurityPropertyView : UserControl
{
	private const string CheckGlyph = "\uE73E";
	private readonly HWND _owner;
	private readonly Action<string> _showError;

	internal ObservableCollection<SecurityPrincipalViewModel> Principals { get; }

	internal WindowsShellSecurityProperties Security { get; }

	internal Visibility EditShieldVisibility => WindowsShellSecurityService.RequiresElevation(Security.ObjectPath) ? Visibility.Visible : Visibility.Collapsed;

	internal SecurityPropertyView(WindowsShellSecurityProperties security, HWND owner, Action<string> showError)
	{
		Security = security;
		_owner = owner;
		_showError = showError;
		Principals = new(security.Principals.Select(static principal => new SecurityPrincipalViewModel(principal)));
		InitializeComponent();
		if (Principals.Count is not 0)
		{
			PrincipalList.SelectedIndex = 0;
		}

		_ = LoadPrincipalIconsAsync();
	}

	private void Advanced_Click(object sender, RoutedEventArgs e)
	{
		ShowResult(WindowsShellSecurityService.ShowAdvancedEditor(_owner, Security.ObjectPath));
	}

	private void Edit_Click(object sender, RoutedEventArgs e)
	{
		ShowResult(WindowsShellSecurityService.ShowPermissionsEditor(_owner, Security.ObjectPath));
	}

	private async Task LoadPrincipalIconsAsync()
	{
		foreach (var principal in Principals)
		{
			await principal.LoadIconAsync();
		}
	}

	private void PrincipalList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		var principal = (PrincipalList.SelectedItem as SecurityPrincipalViewModel)?.Principal;
		PermissionsTitle.Text = principal is null ? string.Empty : string.Format(CultureInfo.CurrentCulture, Strings.PermissionsForFormat.GetLocalized(), principal.Name);
		SetPermission(FullControlAllow, FullControlDeny, principal, 0x000F01FF);
		SetPermission(ModifyAllow, ModifyDeny, principal, 0x000301BF);
		SetPermission(ReadAndExecuteAllow, ReadAndExecuteDeny, principal, 0x000200A9);
		SetPermission(ListFolderContentsAllow, ListFolderContentsDeny, principal, 0x000200A9);
		SetPermission(ReadAllow, ReadDeny, principal, 0x00020089);
		SetPermission(WriteAllow, WriteDeny, principal, 0x00000116);
	}

	private static void SetPermission(TextBlock allow, TextBlock deny, WindowsShellSecurityPrincipal? principal, uint mask)
	{
		allow.Text = principal is not null && HasPermission(principal.AllowedAccessMask, mask) ? CheckGlyph : string.Empty;
		deny.Text = principal is not null && HasPermission(principal.DeniedAccessMask, mask) ? CheckGlyph : string.Empty;
	}

	private static bool HasPermission(uint actual, uint required)
	{
		return (actual & required) == required;
	}

	private void ShowResult(HRESULT result)
	{
		if (result.Failed)
		{
			_showError(new System.Runtime.InteropServices.COMException(null, result.Value).Message);
		}
	}
}

internal sealed class SecurityPrincipalViewModel : INotifyPropertyChanged
{
	private BitmapImage? _icon;

	public BitmapImage? Icon
	{
		get => _icon;
		private set
		{
			if (!ReferenceEquals(_icon, value))
			{
				_icon = value;
				OnPropertyChanged();
			}
		}
	}

	public string Name => Principal.Name;

	internal WindowsShellSecurityPrincipal Principal { get; }

	public event PropertyChangedEventHandler? PropertyChanged;

	internal SecurityPrincipalViewModel(WindowsShellSecurityPrincipal principal)
	{
		Principal = principal;
	}

	internal async Task LoadIconAsync()
	{
		if (Principal.IconData.IsEmpty)
		{
			return;
		}

		Icon = await ThumbnailImageFactory.CreateAsync(Principal.IconData);
	}

	private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}

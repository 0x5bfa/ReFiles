// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using Files.Localization;
using Files.Core.Storage;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Files.ViewModels;

public sealed class BrowseItemViewModel : ObservableObject
{
	private BitmapImage? thumbnail;

	public BrowseItemViewModel(
		string name,
		bool isFolder,
		StorableReference reference)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(reference);
		Name = name;
		IsFolder = isFolder;
		Reference = reference;
	}

	public string Name { get; }

	public bool IsFolder { get; }

	public StorableReference Reference { get; }

	public BitmapImage? Thumbnail
	{
		get => thumbnail;
		private set => SetProperty(ref thumbnail, value);
	}

	public string Kind =>
		(IsFolder ? Strings.Folder : Strings.File).GetLocalized();

	public string ReferenceText =>
		Reference.LastKnownAddress?.Value ?? Reference.ItemId;

	internal void SetThumbnail(BitmapImage? value) => Thumbnail = value;
}

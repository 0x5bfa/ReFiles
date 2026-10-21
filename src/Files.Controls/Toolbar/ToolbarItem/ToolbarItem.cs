// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml.Input;

namespace Files.Controls
{
	/// <summary>
	/// Describes one item in a <see cref="Toolbar"/> and provides change notifications for live updates.
	/// </summary>
	public partial class ToolbarItem : DependencyObject, INotifyPropertyChanged
	{
		private INotifyCollectionChanged? _subItemsCollection;
		private INotifyCollectionChanged? _keyboardAcceleratorsCollection;

		/// <summary>
		/// Initializes a new toolbar item with an empty child-item collection.
		/// </summary>
		public ToolbarItem()
		{
			SubItems = new ObservableCollection<ToolbarItem>();
			KeyboardAccelerators = new ObservableCollection<KeyboardAccelerator>();
		}

		/// <summary>
		/// Occurs when a toolbar item property or its child-item collection changes.
		/// </summary>
		public event PropertyChangedEventHandler? PropertyChanged;

		private void ObserveSubItems(IList<ToolbarItem>? newItems)
		{
			_subItemsCollection?.CollectionChanged -= OnSubItemsCollectionChanged;

			_subItemsCollection = newItems as INotifyCollectionChanged;
			_subItemsCollection?.CollectionChanged += OnSubItemsCollectionChanged;
		}

		private void OnSubItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
		{
			RaisePropertyChanged(nameof(SubItems));
		}

		private void ObserveKeyboardAccelerators(IList<KeyboardAccelerator>? newAccelerators)
		{
			_keyboardAcceleratorsCollection?.CollectionChanged -= OnKeyboardAcceleratorsCollectionChanged;

			_keyboardAcceleratorsCollection = newAccelerators as INotifyCollectionChanged;
			_keyboardAcceleratorsCollection?.CollectionChanged += OnKeyboardAcceleratorsCollectionChanged;
		}

		private void OnKeyboardAcceleratorsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
		{
			RaisePropertyChanged(nameof(KeyboardAccelerators));
		}

		private void RaisePropertyChanged(string propertyName)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}

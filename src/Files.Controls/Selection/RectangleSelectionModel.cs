// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Controls;

internal enum RectangleSelectionMode
{
	Replace,
	Extend,
	Toggle,
}

internal sealed class RectangleSelectionModel
{
	private readonly HashSet<object> _baselineSelection;
	private readonly RectangleSelectionMode _mode;

	internal RectangleSelectionModel(IEnumerable<object> baselineSelection, RectangleSelectionMode mode)
	{
		ArgumentNullException.ThrowIfNull(baselineSelection);

		_baselineSelection = baselineSelection.ToHashSet();
		_mode = mode;
	}

	internal HashSet<object> GetSelection(IEnumerable<object> intersectedItems)
	{
		ArgumentNullException.ThrowIfNull(intersectedItems);

		var selection = _mode is RectangleSelectionMode.Replace ? [] : new HashSet<object>(_baselineSelection);
		foreach (var item in intersectedItems)
		{
			if (_mode is RectangleSelectionMode.Toggle && !selection.Add(item))
			{
				selection.Remove(item);
			}
			else if (_mode is not RectangleSelectionMode.Toggle)
			{
				selection.Add(item);
			}
		}

		return selection;
	}
}

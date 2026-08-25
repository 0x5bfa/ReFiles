// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Controls;

internal sealed class RectangleSelectionNotificationModel
{
	private readonly HashSet<ListViewBase> _changedTargets = [];
	private bool _hasRaisedInitialUpdate;

	internal IReadOnlyList<ListViewBase> RecordChanges(IEnumerable<ListViewBase> changedTargets)
	{
		ArgumentNullException.ThrowIfNull(changedTargets);

		foreach (var target in changedTargets)
		{
			_changedTargets.Add(target);
		}

		if (_hasRaisedInitialUpdate || !_changedTargets.Any(static target => target.SelectedItems.Count is not 0))
		{
			return [];
		}

		_hasRaisedInitialUpdate = true;

		return _changedTargets.ToArray();
	}

	internal IReadOnlyList<ListViewBase> Complete()
	{
		var changedTargets = _changedTargets.ToArray();
		Reset();

		return changedTargets;
	}

	internal void Reset()
	{
		_changedTargets.Clear();
		_hasRaisedInitialUpdate = false;
	}
}

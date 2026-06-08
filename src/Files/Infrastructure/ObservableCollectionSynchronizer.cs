// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Infrastructure;

internal static class ObservableCollectionSynchronizer
{
	public static void Synchronize<T>(
		ObservableCollection<T> target,
		IReadOnlyList<T> desired)
		where T : class
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(desired);

		for (var index = target.Count - 1; index >= 0; index--)
		{
			if (!desired.Contains(target[index]))
			{
				target.RemoveAt(index);
			}
		}

		for (var desiredIndex = 0; desiredIndex < desired.Count; desiredIndex++)
		{
			var item = desired[desiredIndex];
			var currentIndex = target.IndexOf(item);
			if (currentIndex < 0)
			{
				target.Insert(desiredIndex, item);
			}
			else if (currentIndex != desiredIndex)
			{
				target.Move(currentIndex, desiredIndex);
			}
		}
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Sessions;

public sealed partial class TabSession
{
	/// <summary>Gets whether every pane can capture independently restorable content.</summary>
	public bool CanRestore => Panes.All(static pane => pane.Content is IRestorablePaneContent);

	/// <summary>Captures every pane and the layout as one tab restoration state.</summary>
	/// <returns>The captured state, or null if any content cannot be restored.</returns>
	public TabRestoreState? CaptureRestoreState()
	{
		lock (_syncRoot)
		{
			EnsureActive();

			if (_panes.Any(static pane => pane.Content is not IRestorablePaneContent))
			{
				return null;
			}

			var factories = _panes.Select(static pane => ((IRestorablePaneContent)pane.Content).CaptureRestoreFactory()).ToArray();

			return new TabRestoreState(factories, _panes.FindIndex(pane => pane.Id == _activePaneId), _splitOrientation);
		}
	}

	internal static async ValueTask<TabSession> RestoreAsync(IBrowsePaneSessionFactory paneFactory, TabRestoreState state, CancellationToken cancellationToken)
	{
		var panes = new List<PaneSession>();
		try
		{
			foreach (var factory in state.ContentFactories)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var content = factory() ?? throw new InvalidOperationException("The pane content factory returned null.");
				var pane = new PaneSession(content);
				panes.Add(pane);
				await content.InitializeAsync(cancellationToken).ConfigureAwait(false);
			}

			cancellationToken.ThrowIfCancellationRequested();

			var tab = new TabSession(paneFactory, panes[0]);
			tab._panes.AddRange(panes.Skip(1));
			tab._paneSnapshot = Array.AsReadOnly(panes.ToArray());
			tab._activePaneId = panes[state.ActivePaneIndex].Id;
			tab._splitOrientation = state.SplitOrientation;

			return tab;
		}
		catch (Exception creationError)
		{
			var errors = new List<Exception> { creationError };
			foreach (var pane in panes.AsEnumerable().Reverse())
			{
				try
				{
					await pane.DisposeAsync().ConfigureAwait(false);
				}
				catch (Exception cleanupError)
				{
					errors.Add(cleanupError);
				}
			}

			if (errors.Count > 1)
			{
				throw new AggregateException("Tab restoration and cleanup failed.", errors);
			}

			throw;
		}
	}
}

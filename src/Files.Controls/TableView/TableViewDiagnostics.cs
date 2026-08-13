// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Controls;

/// <summary>
/// Provides internal realization diagnostics for <see cref="TableView"/> without expanding its public API surface.
/// </summary>
internal sealed class TableViewDiagnostics : IDisposable
{
	private readonly ITableViewRowsHost _rowsHost;
	private readonly HashSet<ITableViewRow> _realizedRows = new(ReferenceEqualityComparer.Instance);
	private bool _disposed;

	/// <summary>Gets the number of row template roots currently tracked as realized.</summary>
	public int RealizedRowCount => _realizedRows.Count;

	/// <summary>Occurs after a row template root is observed as realized.</summary>
	public event EventHandler<TableViewRowChangingEventArgs>? RowRealized;

	/// <summary>Occurs whenever the tracked realized-row count changes.</summary>
	public event EventHandler? RealizedRowCountChanged;

	/// <summary>Creates diagnostics for the table's current rows host.</summary>
	public TableViewDiagnostics(TableView table)
	{
		ArgumentNullException.ThrowIfNull(table);

		_rowsHost = table.RowsHost ?? throw new InvalidOperationException("TableView does not have a rows host.");
		_rowsHost.RowChanging += RowsHost_RowChanging;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_rowsHost.RowChanging -= RowsHost_RowChanging;
		_realizedRows.Clear();
	}

	private void RowsHost_RowChanging(object? sender, TableViewRowChangingEventArgs e)
	{
		if (e.TemplateRoot is not ITableViewRow row)
		{
			return;
		}

		if (e.InRecycleQueue || e.Item is null)
		{
			if (_realizedRows.Remove(row))
			{
				RealizedRowCountChanged?.Invoke(this, EventArgs.Empty);
			}

			return;
		}

		var added = _realizedRows.Add(row);
		RowRealized?.Invoke(this, e);
		if (added)
		{
			RealizedRowCountChanged?.Invoke(this, EventArgs.Empty);
		}
	}
}

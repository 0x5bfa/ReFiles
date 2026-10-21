// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Files.Core.Windows;

namespace Files.BackgroundServer;

internal sealed class ShellNewItemOperation(Guid id, string folderPath, uint commandOffset, string name) : IServerOperation
{
	public Guid Id => id;
	public string Kind => "Create";
	public int ItemCount => 1;
	public string? DestinationPath => folderPath;
	public bool CanPause => false;

	public async Task ExecuteAsync(OperationExecutionContext context, CancellationToken cancellationToken)
	{
		var menu = new WindowsShellNewMenu(Program.Storage.Value.Source.Scheduler);
		var items = await menu.GetItemsAsync(folderPath, cancellationToken).ConfigureAwait(false);
		if (!items.Any(item => item.CommandOffset == commandOffset && item.Name == name && item.IsEnabled))
		{
			throw new IOException("The requested Shell New menu item changed or is no longer available.");
		}

		context.Report(new OperationProgress(0, 1, CurrentItemName: name));
		if (!await menu.InvokeAsync(folderPath, commandOffset, cancellationToken).ConfigureAwait(false))
		{
			throw new IOException("The Windows Shell could not invoke the New menu item.");
		}

		context.Report(new OperationProgress(1, 1, CurrentItemName: name));
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0
using Files.Core;
using Files.Core.Capabilities;
using Files.Core.Storage;
using Files.Core.Models;

var path = Path.GetTempFileName();
try
{
	var builder = new FilesCoreBuilder().AddWindowsStorage();
	builder.Capabilities.Add<ILabel>(new DelegateCapabilityFactory<ILabel>(static _ => new Label())).SetBorrowedView<ILabel>(static inner => new LabelView(inner));
	await using var runtime = builder.Build();
	await using var item = await runtime.Workspace.ResolveAsync(new StorageAddress("file", path));
	var label = item.Get<ILabel>() ?? throw new InvalidOperationException("Custom capability missing.");
	if (label.Text != "custom" || label is IDisposable)
	{
		throw new InvalidOperationException("Custom borrowed view contract failed.");
	}

	var source = runtime.Workspace.Sources.First();
	var badRegistry = new CapabilityBuilder().Add<ILabel>(new DelegateCapabilityFactory<ILabel>(static _ => new Label()))
		.SetBorrowedView<ILabel>(static inner => new LeakingLabelView(inner)).Build();
	await using var badModel = new StorableModelFactory(badRegistry).Create(source, await source.ResolveAsync(new StorageAddress("file", path)));
	try
	{
		_ = badModel.Get<ILabel>();
		throw new InvalidOperationException("Unrelated interface metadata was lost.");
	}
	catch (InvalidOperationException error) when (error.Message.Contains("exposes unrelated interfaces", StringComparison.Ordinal))
	{
	}

#if !NATIVE_AOT_SMOKE
	foreach (var assembly in new[] { typeof(FilesCoreBuilder).Assembly, typeof(Files.Core.Storage.Windows.WindowsStorageSource).Assembly })
	{
		if (assembly.GetExportedTypes().Any(static type => type.Namespace?.StartsWith("Windows.Win32", StringComparison.Ordinal) == true))
		{
			throw new InvalidOperationException("Native generated types escaped their assembly.");
		}
	}
#endif
}
finally
{
	File.Delete(path);
}

internal interface ILabel : ICapability
{
	string Text { get; }
}

internal sealed class Label : ILabel, IDisposable
{
	public string Text => "custom";

	public void Dispose() { }
}

internal sealed class LabelView(ILabel inner) : ILabel
{
	public string Text => inner.Text;
}

// Intentionally unused outside the view: trimming must still retain this role for validation.
internal interface IUnexpectedRole { }

internal sealed class LeakingLabelView(ILabel inner) : ILabel, IUnexpectedRole
{
	public string Text => inner.Text;
}

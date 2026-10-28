// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using BenchmarkDotNet.Attributes;
using Files.Core.Storage;
using Files.Core.Storage.Windows;

namespace Files.Benchmarks;

/// <summary>Measures fresh Shell menu construction with warmed registration caches.</summary>
[MemoryDiagnoser]
public class WindowsShellContextMenuBenchmarks
{
	private WindowsStorageSource _source = null!;
	private StorableReference[] _selection = [];
	private string _directory = string.Empty;
	private string _file = string.Empty;

	/// <summary>Gets or sets the selected item kind.</summary>
	[Params("File", "Directory")]
	public string SelectionKind { get; set; } = string.Empty;

	/// <summary>Creates the selection and warms the operating-system and registration caches.</summary>
	/// <returns>The asynchronous initialization.</returns>
	[GlobalSetup]
	public async Task Setup()
	{
		_source = new WindowsStorageSource();
		_directory = Path.Combine(Path.GetTempPath(), $"FilesMenuBenchmarks-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_directory);
		_file = Path.Combine(_directory, "benchmark.txt");
		await File.WriteAllTextAsync(_file, "Context-menu benchmark");
		var address = new StorageAddress(WindowsStorageSource.FileAddressScheme, SelectionKind == "File" ? _file : _directory);
		var item = await _source.ResolveAsync(address);
		_selection = [new(_source.SourceId, item.Id, address)];
		await using var session = await WindowsShellContextMenuSession.CreateAsync(_source, _selection);
		Console.WriteLine($"{SelectionKind}: {session?.Entries.Count ?? 0} root entries; native state is rebuilt for every iteration.");
	}

	/// <summary>Constructs and disposes a complete popup session without reusing selection state.</summary>
	/// <returns>The number of root entries.</returns>
	[Benchmark]
	public async Task<int> ConstructMenu()
	{
		await using var session = await WindowsShellContextMenuSession.CreateAsync(_source, _selection);

		return session?.Entries.Count ?? 0;
	}

	/// <summary>Disposes the source and removes the temporary selection.</summary>
	/// <returns>The asynchronous cleanup.</returns>
	[GlobalCleanup]
	public async Task Cleanup()
	{
		await _source.DisposeAsync();
		File.Delete(_file);
		Directory.Delete(_directory);
	}
}

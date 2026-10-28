// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using BenchmarkDotNet.Attributes;
using Files.Core.Storage.Windows;

namespace Files.Benchmarks;

/// <summary>
/// Measures Windows File Explorer app-extension catalog lookups with and without the Files-side cache.
/// </summary>
[MemoryDiagnoser]
public class WindowsFileExplorerAppExtensionCatalogBenchmarks
{
	private static readonly string[] _fileItemTypes = [".txt", "*"];
	private static readonly string[] _directoryItemTypes = ["Directory"];
	private IWindowsShellScheduler _scheduler = null!;
	private IReadOnlyList<string> _itemTypes = null!;

	/// <summary>Gets or sets the kind of Shell selection to enumerate.</summary>
	[Params("File", "Directory")]
	public string SelectionKind { get; set; } = string.Empty;

	/// <summary>Initializes the Shell STA scheduler and warms the Shell State Repository cache.</summary>
	[GlobalSetup]
	public async Task Setup()
	{
		_itemTypes = SelectionKind switch
		{
			"File" => _fileItemTypes,
			"Directory" => _directoryItemTypes,
			_ => throw new InvalidOperationException($"Unknown selection kind '{SelectionKind}'.")
		};

		_scheduler = new WindowsShellScheduler();
		await _scheduler.InvokeOperationAsync(() => WindowsFileExplorerAppExtensionCatalog.GetRegistrations(_itemTypes).Count);
	}

	/// <summary>Clears the Files-side cache before each uncached lookup.</summary>
	[IterationSetup(Target = nameof(CacheMiss))]
	public void ResetCache()
	{
		WindowsFileExplorerAppExtensionCatalog.ResetCache();
	}

	/// <summary>Warms the Files-side cache before each cached lookup.</summary>
	[IterationSetup(Target = nameof(CacheHit))]
	public void WarmCache()
	{
		_scheduler.InvokeOperationAsync(() => WindowsFileExplorerAppExtensionCatalog.GetRegistrations(_itemTypes).Count).GetAwaiter().GetResult();
	}

	/// <summary>Measures a lookup after clearing the Files-side cache.</summary>
	/// <returns>The number of registrations found.</returns>
	[Benchmark(Baseline = true)]
	public Task<int> CacheMiss()
	{
		return _scheduler.InvokeOperationAsync(() => WindowsFileExplorerAppExtensionCatalog.GetRegistrations(_itemTypes).Count);
	}

	/// <summary>Measures a lookup using the warmed Files-side cache.</summary>
	/// <returns>The number of registrations found.</returns>
	[Benchmark]
	public Task<int> CacheHit()
	{
		return _scheduler.InvokeOperationAsync(() => WindowsFileExplorerAppExtensionCatalog.GetRegistrations(_itemTypes).Count);
	}

	/// <summary>Disposes the Shell STA scheduler.</summary>
	[GlobalCleanup]
	public ValueTask Cleanup()
	{
		return _scheduler.DisposeAsync();
	}
}

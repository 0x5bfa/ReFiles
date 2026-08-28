// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using BenchmarkDotNet.Running;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using Files.Benchmarks;
using System.IO;
using System.Text.Json;

if (args.Contains("--browse-scenario", StringComparer.OrdinalIgnoreCase))
{
	foreach (var itemCount in new[] { 100, 1_000, 10_000, 44_000 })
	{
		var results = new List<BrowsePipelineScenarioResult>();
		for (var iteration = 0; iteration < 5; iteration++)
		{
			results.Add(await BrowsePipelineScenarioRunner.RunAsync(itemCount));
		}

		var ordered = results.OrderBy(static result => result.TotalMilliseconds).ToArray();
		Console.WriteLine(JsonSerializer.Serialize(ordered[ordered.Length / 2]));
	}

	return;
}

var smoke = args.Contains("--smoke", StringComparer.OrdinalIgnoreCase);
var benchmarkArgs = args
	.Where(argument => !argument.Equals("--smoke", StringComparison.OrdinalIgnoreCase))
	.ToArray();
var baseJob = smoke
	? Job.Dry
	: Job.Default;
var benchmarkJob = baseJob.WithMsBuildArguments(
	"/p:MinimalWindowsVersion=10.0.19041.0",
	"/p:TargetPlatformMinVersion=10.0.19041.0",
	"/p:WindowsTargetFramework=net10.0-windows10.0.26100.0",
	"/p:Platform=x64",
	"/p:PlatformTarget=x64");
var artifactsPath = Path.Combine(Path.GetTempPath(), $"ReFiles.Benchmarks-{Environment.ProcessId}");

var config = ManualConfig
	.CreateEmpty()
	.AddLogger(ConsoleLogger.Default)
	.AddColumnProvider(DefaultColumnProviders.Instance)
	.AddExporter(MarkdownExporter.GitHub)
	.AddJob(benchmarkJob)
	.WithArtifactsPath(artifactsPath);

if (smoke && benchmarkArgs.Length is 0)
{
	BenchmarkRunner.Run<CapabilityResolutionBenchmarks>(config);
	BenchmarkRunner.Run<BrowsePipelineBenchmarks>(config);
	BenchmarkRunner.Run<TableViewColumnLayoutBenchmarks>(config);
}
else
{
	BenchmarkSwitcher
		.FromAssembly(typeof(Program).Assembly)
		.Run(benchmarkArgs, config);
}

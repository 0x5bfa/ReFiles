// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using BenchmarkDotNet.Running;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using Files.Benchmarks;

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

var config = ManualConfig
	.CreateEmpty()
	.AddLogger(ConsoleLogger.Default)
	.AddColumnProvider(DefaultColumnProviders.Instance)
	.AddExporter(MarkdownExporter.GitHub)
	.AddJob(benchmarkJob);

if (smoke)
{
	BenchmarkRunner.Run<ItemFeatureResolutionBenchmarks>(config);
}
else
{
	BenchmarkSwitcher
		.FromAssembly(typeof(Program).Assembly)
		.Run(benchmarkArgs, config);
}

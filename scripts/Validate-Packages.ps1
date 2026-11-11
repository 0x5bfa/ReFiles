# Copyright (c) Files Community
# SPDX-License-Identifier: MPL-2.0
param([string]$Configuration = 'Release', [ValidateSet('x64', 'arm64')][string]$Platform = 'x64', [string]$OutputDirectory = "$PSScriptRoot/../artifacts/packages")
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$projects = @('Files.Core.Contracts', 'Files.Core.ProviderContracts', 'Files.Core', 'Files.Providers', 'Files.Hosting')
foreach ($project in $projects) {
    & msbuild "$repo/src/$project/$project.csproj" -restore -p:Configuration=$Configuration -p:Platform=$Platform -p:PackageOutputPath=$OutputDirectory -v:quiet -clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { throw "Package build failed: $project" }
}
$projectionVersion = ([xml](Get-Content -LiteralPath "$repo/Directory.Build.props" -Raw)).SelectSingleNode("//WindowsSdkPackageVersion").InnerText
$versions = @()
foreach ($project in $projects) {
    $id = $project -replace '^Files', 'ReFiles'
    $packages = @(Get-ChildItem -LiteralPath $OutputDirectory -Filter "$id.*.nupkg" | Where-Object { $_.Name -match "^$([regex]::Escape($id))\.\d" })
    if ($packages.Count -ne 1) { throw "Expected exactly one package for $id; use an empty output directory." }
    $archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
    try {
        $entry = @($archive.Entries | Where-Object FullName -Like '*.nuspec')[0]
        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml]$spec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($spec.package.metadata.id -ne $id) { throw "Unexpected package ID in $($packages[0].Name)" }
        $versions += [string]$spec.package.metadata.version
        $dependencies = @($spec.package.metadata.dependencies.group.dependency)
        foreach ($dependency in $dependencies | Where-Object id -Like 'ReFiles.*') {
            if ($dependency.version.Trim('[', ']') -ne $spec.package.metadata.version) { throw "Mismatched dependency in $id" }
        }
    } finally { $archive.Dispose() }
}
if (@($versions | Select-Object -Unique).Count -ne 1) { throw 'All package versions must match.' }
# Consumers live outside the repository so central properties and project references cannot mask missing package assets.
$consumerRoot = Join-Path ([IO.Path]::GetTempPath()) "ReFiles.PackageConsumers.$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $consumerRoot | Out-Null
$feed = [Security.SecurityElement]::Escape($OutputDirectory)
$nativeFeed = [Security.SecurityElement]::Escape("$repo/src/Files/Assets/Libraries")
@"
<configuration><packageSources><clear/><add key="packages" value="$feed"/><add key="native" value="$nativeFeed"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>
"@ | Set-Content -LiteralPath "$consumerRoot/NuGet.Config"
foreach ($kind in @('Hosting', 'Provider', 'Sdk')) {
    $directory = Join-Path $consumerRoot $kind
    New-Item -ItemType Directory -Path $directory | Out-Null
    $references = switch ($kind) {
        Hosting { '<PackageReference Include="ReFiles.Hosting" Version="{0}" />' -f $versions[0] }
        Provider { '<PackageReference Include="ReFiles.Core" Version="{0}" /><PackageReference Include="ReFiles.Providers" Version="{0}" />' -f $versions[0] }
        Sdk { '<PackageReference Include="ReFiles.Core.ProviderContracts" Version="{0}" />' -f $versions[0] }
    }
    $outputType = if ($kind -eq 'Sdk') { 'Library' } else { 'Exe' }
    @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0-windows10.0.26100.0</TargetFramework><OutputType>$outputType</OutputType><WindowsSdkPackageVersion>$projectionVersion</WindowsSdkPackageVersion><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RuntimeIdentifier>win-$Platform</RuntimeIdentifier><NuGetAudit>false</NuGetAudit><RestorePackagesPath>$consumerRoot/packages</RestorePackagesPath></PropertyGroup><ItemGroup>$references</ItemGroup></Project>
"@ | Set-Content -LiteralPath "$directory/Consumer.csproj"
    Copy-Item -LiteralPath "$repo/tests/PackageConsumers/$kind.cs" -Destination "$directory/Program.cs"
    & dotnet build "$directory/Consumer.csproj" -c Release --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "$kind package consumer failed to build: $directory" }
    if ($kind -eq 'Hosting') {
        $rejection = & dotnet msbuild "$directory/Consumer.csproj" -t:ValidateReFilesWindowsSdkProjection -p:WindowsSdkPackageVersion=10.0.26100.57 -nologo 2>&1
        if ($LASTEXITCODE -eq 0 -or "$rejection" -notlike '*is older than the ReFiles projection*') {
            throw 'The packaged SDK projection guard did not reject an incompatible consumer.'
        }
    }
    if ($kind -ne 'Sdk' -and $Platform -eq [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()) {
        & dotnet "$directory/bin/Release/net10.0-windows10.0.26100.0/win-$Platform/Consumer.dll"
        if ($LASTEXITCODE -ne 0) { throw "$kind package smoke test failed: $directory" }
    }
    if ($kind -eq 'Provider' -and $Platform -eq [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()) {
        & dotnet publish "$directory/Consumer.csproj" -c Release -p:PublishAot=true -p:TrimmerSingleWarn=false -p:TreatWarningsAsErrors=true -p:DefineConstants=NATIVE_AOT_SMOKE -o "$directory/aot" --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw 'The extensibility consumer failed Native AOT publish.' }
        & "$directory/aot/Consumer.exe"
        if ($LASTEXITCODE -ne 0) { throw 'The extensibility consumer failed Native AOT execution.' }
    }
    $assets = Get-Content -LiteralPath "$directory/obj/project.assets.json" -Raw | ConvertFrom-Json -AsHashtable
    foreach ($target in $assets.targets.Values) {
        foreach ($library in $target.GetEnumerator()) {
            $name = $library.Key.Split('/')[0]
            $forbidden = if ($kind -eq 'Hosting') { @('ReFiles.Core', 'ReFiles.Providers', 'ReFiles.Core.ProviderContracts', 'FluentFTP', 'SevenZipSharp', 'System.Management') } elseif ($kind -eq 'Sdk') { @('ReFiles.Core', 'ReFiles.Providers') } else { @() }
            if ($kind -eq 'Sdk' -and $name -in $forbidden) {
                throw "ProviderContracts unexpectedly depends on $name"
            }
            if ($name -in $forbidden -and @($library.Value.compile.Keys | Where-Object { $_ -ne 'lib/_._' -and $_ -notlike '*/_._' }).Count -gt 0) {
                throw "$kind leaked compile assets from $name"
            }
        }
    }
}
Write-Output "Validated five packages and package consumers. Consumer artifacts: $consumerRoot"

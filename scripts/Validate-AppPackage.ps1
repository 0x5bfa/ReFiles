# Copyright (c) Files Community
# SPDX-License-Identifier: MPL-2.0
param([ValidateSet('x64', 'arm64')][string]$Platform = 'x64', [string]$OutputDirectory = "$PSScriptRoot/../artifacts/msix/$Platform")
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
& msbuild "$repo/src/Files/Files.csproj" -restore -t:Publish -p:Configuration=Release -p:Platform=$Platform -p:BuildUnsignedAppPackage=true -p:TrimmerSingleWarn=false "-p:AppxPackageDir=$OutputDirectory/" -v:quiet -clp:ErrorsOnly
if ($LASTEXITCODE -ne 0) { throw 'Native AOT MSIX publish failed.' }
$packages = @(Get-ChildItem -LiteralPath $OutputDirectory -Filter '*.msix' -Recurse | Where-Object Name -NotLike '*Dependencies*')
if ($packages.Count -ne 1) { throw 'Expected exactly one application MSIX; use an empty output directory.' }
$archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
try {
    $machine = if ($Platform -eq 'x64') { 0x8664 } else { 0xaa64 }
    $paths = @('Files.exe', 'FilesLauncher.exe', 'Files.BackgroundServer/Files.BackgroundServer.exe')
    $nativeName = if ($Platform -eq 'x64') { '7z64.dll' } else { '7zArm64.dll' }
    $nativeEntries = @($archive.Entries | Where-Object { [IO.Path]::GetFileName($_.FullName) -eq $nativeName })
    if ($nativeEntries.Count -eq 0) { throw "MSIX is missing $nativeName" }
    $paths += $nativeEntries.FullName
    foreach ($path in $paths) {
        $entry = $archive.GetEntry($path)
        if ($null -eq $entry) { throw "Missing MSIX payload: $path" }
        if ($path -like '*.exe' -and $null -ne $archive.GetEntry([IO.Path]::ChangeExtension($path, '.dll'))) {
            throw "Managed entry assembly found beside Native AOT executable: $path"
        }
        $stream = $entry.Open()
        $buffer = [IO.MemoryStream]::new()
        try { $stream.CopyTo($buffer); $bytes = $buffer.ToArray() } finally { $stream.Dispose(); $buffer.Dispose() }
        $pe = [BitConverter]::ToInt32($bytes, 0x3c)
        if ([BitConverter]::ToUInt16($bytes, $pe + 4) -ne $machine) { throw "Wrong native architecture: $path" }
        # PE32+ optional header: CLR runtime header is data-directory entry 14.
        if ($path -like '*.exe' -and [BitConverter]::ToUInt32($bytes, $pe + 24 + 112 + 14 * 8) -ne 0) {
            throw "Expected a Native AOT executable: $path"
        }
    }
    $manifest = $archive.GetEntry('AppxManifest.xml')
    if ($null -eq $manifest) { throw 'Missing AppxManifest.xml' }
    $reader = [IO.StreamReader]::new($manifest.Open())
    try { $manifestText = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($manifestText -notlike '*Files.BackgroundServer\Files.BackgroundServer.exe*') { throw 'WinRT server registration is missing from the manifest.' }
    Write-Output "Validated Native AOT executables, native archive assets, and WinRT server registration: $($packages[0].FullName)"
} finally { $archive.Dispose() }

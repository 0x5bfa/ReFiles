# Copyright (c) Files Community
# SPDX-License-Identifier: MPL-2.0

# Rebuild the vendored desktop package without changing upstream source.
$ErrorActionPreference = 'Stop'
$revision = '9725bda53e1ee018b39a51c268a90c4905915926'
$checkout = Join-Path $env:TEMP "ReFiles-SevenZipSharp-$($revision.Substring(0, 7))"
$output = Join-Path $PSScriptRoot '../src/Files/Assets/Libraries'

if (!(Test-Path -LiteralPath $checkout)) {
	git clone --quiet https://github.com/files-community/SevenZipSharp.git $checkout
	if ($LASTEXITCODE -ne 0) { throw 'Could not clone SevenZipSharp.' }
}

git -C $checkout checkout --quiet $revision
if ($LASTEXITCODE -ne 0) { throw 'Could not select the pinned SevenZipSharp revision.' }

if (git -C $checkout status --porcelain) { throw 'The build checkout has local changes.' }

dotnet build (Join-Path $checkout 'SevenZip/SevenZip.csproj') -c Desktop -p:Version=1.0.3-desktop.1 -p:Optimize=true "-p:PackageOutputPath=$output" -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Could not build the desktop package.' }

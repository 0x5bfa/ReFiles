# Copyright (c) Files Community
# SPDX-License-Identifier: MPL-2.0

[CmdletBinding()]
param(
	[ValidateSet('x64', 'arm64')]
	[string] $Platform = 'x64',
	[switch] $NoLaunch
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repositoryRoot 'src\Files\Files.csproj'
$vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswherePath))
{
	throw 'Visual Studio Installer (vswhere.exe) was not found.'
}

$visualStudioInstallPath = & $vswherePath -latest -products * -requires Microsoft.Component.MSBuild -property installationPath | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($visualStudioInstallPath))
{
	throw 'A Visual Studio installation with MSBuild was not found.'
}

$devShellModulePath = Join-Path $visualStudioInstallPath 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll'
Import-Module $devShellModulePath
Enter-VsDevShell -VsInstallPath $visualStudioInstallPath -SkipAutomaticLocation -DevCmdArguments "-arch=$Platform -host_arch=$Platform"

Push-Location $repositoryRoot
try
{
	& dotnet publish $projectPath -c Release -p:Platform=$Platform -p:PublishProfile="win-$Platform" -v:quiet -clp:ErrorsOnly
	if ($LASTEXITCODE -ne 0)
	{
		throw "Native AOT publish failed with exit code $LASTEXITCODE."
	}

	$platformOutputRoot = Join-Path $repositoryRoot "src\Files\bin\$Platform\Release"
	$appManifest = Get-ChildItem $platformOutputRoot -Recurse -Filter AppxManifest.xml | Where-Object { Test-Path -LiteralPath (Join-Path $_.DirectoryName 'publish\Files.exe') } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
	if ($null -eq $appManifest)
	{
		throw 'The published Files.exe and its AppX manifest were not found.'
	}

	$publishDirectory = Join-Path $appManifest.DirectoryName 'publish'
	$publishedManifestPath = Join-Path $publishDirectory 'AppxManifest.xml'
	Copy-Item -LiteralPath $appManifest.FullName -Destination $publishedManifestPath -Force
	Add-AppxPackage -Register $publishedManifestPath -ForceApplicationShutdown
	Write-Host "Native AOT app registered from $publishDirectory"

	if (-not $NoLaunch)
	{
		[xml] $manifest = Get-Content -LiteralPath $publishedManifestPath -Raw
		$packageName = $manifest.Package.Identity.Name
		$package = Get-AppxPackage -Name $packageName | Sort-Object Version -Descending | Select-Object -First 1
		if ($null -eq $package)
		{
			throw "The registered package '$packageName' was not found."
		}

		Start-Process explorer.exe -ArgumentList "shell:AppsFolder\$($package.PackageFamilyName)!App"
	}
}
finally
{
	Pop-Location
}

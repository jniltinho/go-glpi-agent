#Requires -Version 5.1
<#
  Build the development MSI on the Windows guest when the host cannot run WiX.
  Expects staged publish output and packaging sources under C:\dotnet-glpi-agent-test\src.
#>
[CmdletBinding()]
param(
    [string]$Root = 'C:\dotnet-glpi-agent-test',
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
$src = Join-Path $Root 'src'
$publish = Join-Path $src 'artifacts\publish\win-x64'
$wixproj = Join-Path $src 'packaging\wix\DotnetGlpiAgent.Package.wixproj'
$outMsiDir = Join-Path $Root 'package'
$sdkDir = Join-Path $Root 'dotnet-sdk'

function Ensure-DotNetSdk {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) {
        $ver = & dotnet --version 2>$null
        Write-Host "Using existing dotnet $ver"
        return
    }

    New-Item -ItemType Directory -Force -Path $sdkDir | Out-Null
    $installPs1 = Join-Path $sdkDir 'dotnet-install.ps1'
    if (-not (Test-Path $installPs1)) {
        Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installPs1
    }
    & powershell -NoProfile -ExecutionPolicy Bypass -File $installPs1 -Channel 10.0 -InstallDir $sdkDir
    $env:DOTNET_ROOT = $sdkDir
    $env:PATH = "$sdkDir;$env:PATH"
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'dotnet SDK installation failed'
    }
    Write-Host "Installed dotnet $(& dotnet --version)"
}

if (-not (Test-Path (Join-Path $publish 'dotnet-glpi-agent.exe'))) {
    throw "Publish output missing at $publish"
}
if (-not (Test-Path $wixproj)) {
    throw "WiX project missing at $wixproj"
}

Ensure-DotNetSdk
New-Item -ItemType Directory -Force -Path $outMsiDir | Out-Null

# Drop any host-copied obj/ cache that can poison the Windows build.
Remove-Item -Recurse -Force (Join-Path $src 'packaging\wix\obj') -ErrorAction SilentlyContinue

$harvest = Join-Path $src 'packaging\scripts\Generate-HarvestedFiles.ps1'
$harvestOut = Join-Path $src 'packaging\wix\HarvestedFiles.wxs'
& powershell -NoProfile -ExecutionPolicy Bypass -File $harvest -PublishDir $publish -OutputPath $harvestOut

# Point the wixproj PublishDir at staged publish and emit MSI into guest package dir.
# Avoid trailing backslash (breaks MSBuild/PowerShell quoting).
$publishTrim = $publish.TrimEnd('\')
$outTrim = $outMsiDir.TrimEnd('\')
& dotnet build $wixproj `
    -c Release `
    -p:ProductVersion=$Version `
    -p:SkipPublishAgent=true `
    -p:SkipHarvest=true `
    "-p:PublishDir=$publishTrim" `
    "-p:OutputPath=$outTrim\\"

$msi = Get-ChildItem -Path $outMsiDir -Filter 'dotnet-glpi-agent-*-win-x64.msi' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msi) {
    throw "MSI was not produced under $outMsiDir"
}

# Canonical name for provision.ps1
Copy-Item -LiteralPath $msi.FullName -Destination (Join-Path $outMsiDir 'agent.msi') -Force
Write-Host "MSI ready: $($msi.FullName)"
Write-Host "Also: $(Join-Path $outMsiDir 'agent.msi')"

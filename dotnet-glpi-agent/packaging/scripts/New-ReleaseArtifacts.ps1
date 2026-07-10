#Requires -Version 5.1
<#
.SYNOPSIS
  Produce checksums, SBOM snapshot, and notices for packaging artifacts on Windows.
#>
[CmdletBinding()]
param(
    [string]$Version = '0.1.0',
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
if (-not $OutDir) {
    $OutDir = Join-Path $Root "artifacts\release\$Version"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$msiDir = Join-Path $Root 'artifacts\msi'
$publishDir = Join-Path $Root 'artifacts\publish\win-x64'

if (Test-Path $msiDir) {
    Get-ChildItem -Path $msiDir -File | Where-Object {
        $_.Extension -eq '.msi' -or $_.Name -like '*.UNSIGNED.txt'
    } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $OutDir -Force
    }
}

if (Test-Path (Join-Path $publishDir 'dotnet-glpi-agent.exe')) {
    $portable = Join-Path $OutDir 'publish-win-x64'
    New-Item -ItemType Directory -Force -Path $portable | Out-Null
    Copy-Item (Join-Path $publishDir 'dotnet-glpi-agent.exe') $portable -Force
    $deps = Join-Path $publishDir 'dotnet-glpi-agent.deps.json'
    if (Test-Path $deps) { Copy-Item $deps $portable -Force }
}

Copy-Item (Join-Path $Root 'THIRD-PARTY-NOTICES.md') (Join-Path $OutDir 'THIRD-PARTY-NOTICES.md') -Force
Copy-Item (Join-Path $Root 'LICENSE') (Join-Path $OutDir 'LICENSE') -Force

$sbom = Join-Path $OutDir 'sbom-dotnet-packages.txt'
$header = @(
    '# Dotnet GLPI Agent dependency snapshot'
    "# Generated: $([DateTime]::UtcNow.ToString('o'))"
    "# Version: $Version"
    ''
)
$list = & dotnet list (Join-Path $Root 'DotnetGlpiAgent.sln') package --include-transitive 2>$null
Set-Content -LiteralPath $sbom -Value ($header + $list) -Encoding utf8

$sums = Join-Path $OutDir 'SHA256SUMS'
Get-ChildItem -Path $OutDir -Recurse -File | Where-Object { $_.Name -ne 'SHA256SUMS' } | Sort-Object FullName | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $relative = $_.FullName.Substring($OutDir.Length).TrimStart('\', '/')
    '{0}  {1}' -f $hash, $relative
} | Set-Content -LiteralPath $sums -Encoding ascii

Write-Host "Release artifacts written to $OutDir"

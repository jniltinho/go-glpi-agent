#Requires -Version 5.1
<#
  Optional three-agent comparison: Dotnet vs Go vs official GLPI Agent.
  Emits stable-field and section-count JSON under OutDir.
#>
[CmdletBinding()]
param(
    [string]$OutDir = 'C:\dotnet-glpi-agent-test\out',
    [string]$RefDir = 'C:\dotnet-glpi-agent-test\ref',
    [string]$DotnetLocal = 'C:\dotnet-glpi-agent-test\out\local'
)

$ErrorActionPreference = 'Continue'
$sections = @(
    'HARDWARE', 'BIOS', 'OPERATINGSYSTEM', 'CPUS', 'MEMORIES', 'DRIVES', 'STORAGES',
    'NETWORKS', 'SOFTWARES', 'USBDEVICES', 'LOCAL_USERS', 'LOCAL_GROUPS', 'PRINTERS',
    'MONITORS', 'VIDEOS', 'CONTROLLERS', 'BATTERIES', 'ANTIVIRUS', 'FIREWALL', 'FIREWALLS',
    'SOUNDS', 'INPUTS', 'PORTS'
)

function Get-XmlSectionCounts([string]$Directory) {
    $counts = @{}
    foreach ($s in $sections) { $counts[$s] = 0 }
    $xml = Get-ChildItem -Path $Directory -Filter *.xml -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $xml) { return $counts }
    $text = Get-Content -LiteralPath $xml.FullName -Raw
    foreach ($s in $sections) {
        $counts[$s] = ([regex]::Matches($text, "<$s(?:\s|>)")).Count
    }
    # Canonicalise FIREWALLS → FIREWALL for cross-agent comparison.
    if (($counts['FIREWALLS'] -as [int]) -gt 0 -and ($counts['FIREWALL'] -as [int]) -eq 0) {
        $counts['FIREWALL'] = $counts['FIREWALLS']
    }
    return $counts
}

function Test-CoverageThresholds([hashtable]$Dotnet, [hashtable]$Go, [hashtable]$Perl) {
    $failures = @()
    # Dotnet must not lag behind Go on core categories.
    foreach ($s in @('OPERATINGSYSTEM','BIOS','HARDWARE','CPUS','SOFTWARES','NETWORKS','LOCAL_GROUPS')) {
        if ([int]$Dotnet[$s] -lt [int]$Go[$s]) {
            $failures += "dotnet $s ($($Dotnet[$s])) < go ($($Go[$s]))"
        }
    }
    # Software within 10 of Perl when Perl is present.
    if ([int]$Perl['SOFTWARES'] -gt 0) {
        $delta = [Math]::Abs([int]$Dotnet['SOFTWARES'] - [int]$Perl['SOFTWARES'])
        if ($delta -gt 10) {
            $failures += "dotnet softwares delta vs perl is $delta (>10)"
        }
    }
    return $failures
}

function Get-JsonStableFields([string]$Directory) {
    $json = Get-ChildItem -Path $Directory -Filter *.json -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $json) { return $null }
    try {
        $doc = Get-Content -LiteralPath $json.FullName -Raw | ConvertFrom-Json
        return [ordered]@{
            deviceid = $doc.deviceid
            name = $doc.content.hardware.name
            uuid = $doc.content.hardware.uuid
            os = $doc.content.operatingsystem.name
            arch = $doc.content.operatingsystem.arch
            bios_serial = $doc.content.bios.ssn
            cpu_count = @($doc.content.cpus).Count
            memory_slots = @($doc.content.memories).Count
            networks = @($doc.content.networks).Count
            softwares = @($doc.content.softwares).Count
        }
    } catch {
        return @{ error = "$_" }
    }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Run Go reference if staged
$go = Join-Path $RefDir 'go-glpi-agent.exe'
$goOut = Join-Path $OutDir 'ref-go'
if (Test-Path $go) {
    New-Item -ItemType Directory -Force -Path $goOut | Out-Null
    & $go run --local $goOut --debug *> (Join-Path $OutDir 'ref-go.log')
}

# Run official Perl portable if staged (either staging name)
$zip = @('glpi-agent-ref.zip', 'GLPI-Agent-portable.zip') |
    ForEach-Object { Join-Path $RefDir $_ } |
    Where-Object { Test-Path $_ } |
    Select-Object -First 1
if (-not $zip) { $zip = Join-Path $RefDir 'glpi-agent-ref.zip' }
$perlOut = Join-Path $OutDir 'ref-perl'
if (Test-Path $zip) {
    $extract = Join-Path $RefDir 'glpi-agent-ref'
    if (-not (Test-Path $extract)) {
        Expand-Archive -Force $zip $extract
    }
    $bat = Get-ChildItem $extract -Filter 'glpi-agent.bat' -Recurse | Select-Object -First 1
    if ($bat) {
        New-Item -ItemType Directory -Force -Path $perlOut | Out-Null
        & $bat.FullName "--local=$perlOut" '--no-task=deploy,wakeonlan' *> (Join-Path $OutDir 'ref-perl.log')
    }
}

$dotnetCounts = Get-XmlSectionCounts $DotnetLocal
$goCounts = Get-XmlSectionCounts $goOut
$perlCounts = Get-XmlSectionCounts $perlOut
$thresholdFailures = Test-CoverageThresholds $dotnetCounts $goCounts $perlCounts

$report = [ordered]@{
    generated_utc = [DateTime]::UtcNow.ToString('o')
    dotnet_stable = Get-JsonStableFields $DotnetLocal
    counts = [ordered]@{
        dotnet = $dotnetCounts
        go = $goCounts
        perl = $perlCounts
    }
    threshold_failures = $thresholdFailures
    status = $(if ($thresholdFailures.Count -eq 0) { 'passed' } else { 'failed' })
}

$reportPath = Join-Path $OutDir 'comparison.json'
($report | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Host "Wrote $reportPath status=$($report.status)"
if ($thresholdFailures.Count -gt 0) {
    $thresholdFailures | ForEach-Object { Write-Warning $_ }
    exit 1
}

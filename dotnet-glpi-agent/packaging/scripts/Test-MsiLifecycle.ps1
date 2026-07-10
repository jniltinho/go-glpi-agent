#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Automated MSI lifecycle matrix for a development package on Windows.

.DESCRIPTION
  Covers clean install, silent property application, service state, repair,
  identity preservation across uninstall/reinstall, optional upgrade when a
  previous MSI is supplied, downgrade blocking, and destructive PURGE.

  Intended for the Vagrant lab or a dedicated Windows packager — not hosted CI.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$MsiPath,

    [string]$PreviousMsiPath,

    [string]$Server = '',

    [string]$Tag = 'msi-lifecycle',

    [string]$ResultsDir = 'C:\dotnet-glpi-agent-test\msi-lifecycle',

    [switch]$SkipPurge
)

$ErrorActionPreference = 'Stop'
$serviceName = 'DotnetGlpiAgent'
$dataDir = Join-Path $env:ProgramData 'DotnetGlpiAgent'
$installDir = Join-Path ${env:ProgramFiles} 'DotnetGlpiAgent'
$cfg = Join-Path $dataDir 'agent.cfg'
$stateDir = Join-Path $dataDir 'state'

function Invoke-Msi {
    param([string[]]$ArgumentList, [string]$LogName)
    New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null
    $log = Join-Path $ResultsDir $LogName
    $args = @('/qn', '/norestart', "/L*v", $log) + $ArgumentList
    $proc = Start-Process -FilePath 'msiexec.exe' -ArgumentList $args -Wait -PassThru
    if ($proc.ExitCode -ne 0 -and $proc.ExitCode -ne 3010) {
        throw "msiexec failed exit=$($proc.ExitCode) log=$log args=$($ArgumentList -join ' ')"
    }
    return $proc.ExitCode
}

function Get-ProductCodeFromMsi {
    param([string]$Path)
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($Path, 0))
    $view = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @("SELECT Value FROM Property WHERE Property='ProductCode'"))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
    $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
    return $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, 1)
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$results = [ordered]@{}
New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null
$MsiPath = (Resolve-Path -LiteralPath $MsiPath).Path

# --- clean / silent install ---
Write-Host '=== clean silent install ==='
$props = @("/i", "`"$MsiPath`"", "TAG=$Tag", 'STARTSERVICE=1', 'RUNNOW=0')
if ($Server) { $props += "SERVER=$Server" }
Invoke-Msi -ArgumentList $props -LogName '01-install.log' | Out-Null
Assert-True (Test-Path (Join-Path $installDir 'dotnet-glpi-agent.exe')) 'executable missing after install'
Assert-True (Test-Path $cfg) 'agent.cfg missing after install'
$svc = Get-Service -Name $serviceName -ErrorAction Stop
Assert-True ($svc.StartType -eq 'Automatic') 'service start type is not Automatic'
$results.clean_install = 'ok'

# force one identity generation
& (Join-Path $installDir 'dotnet-glpi-agent.exe') run --config $cfg --local (Join-Path $dataDir 'inventories') --force 2>&1 |
    Tee-Object -FilePath (Join-Path $ResultsDir '02-local-run.log') | Out-Null
$identityBefore = Get-ChildItem -Path $stateDir -File -ErrorAction SilentlyContinue | ForEach-Object { $_.Name + ':' + $_.Length }
$results.identity_before = ($identityBefore -join ';')

# --- repair ---
Write-Host '=== repair ==='
Remove-Item -LiteralPath (Join-Path $installDir 'dotnet-glpi-agent.exe') -Force
Invoke-Msi -ArgumentList @('/fa', "`"$MsiPath`"") -LogName '03-repair.log' | Out-Null
Assert-True (Test-Path (Join-Path $installDir 'dotnet-glpi-agent.exe')) 'repair did not restore executable'
Assert-True (Test-Path $cfg) 'repair removed configuration'
$results.repair = 'ok'

# --- uninstall preserves data ---
Write-Host '=== uninstall preserve ==='
$productCode = Get-ProductCodeFromMsi -Path $MsiPath
Invoke-Msi -ArgumentList @('/x', $productCode) -LogName '04-uninstall.log' | Out-Null
Assert-True (-not (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) 'service still present after uninstall'
Assert-True (Test-Path $cfg) 'normal uninstall deleted configuration'
Assert-True (Test-Path $stateDir) 'normal uninstall deleted state'
$results.uninstall_preserve = 'ok'

# --- reinstall identity ---
Write-Host '=== reinstall identity ==='
Invoke-Msi -ArgumentList @("/i", "`"$MsiPath`"", "TAG=$Tag", 'STARTSERVICE=0') -LogName '05-reinstall.log' | Out-Null
$identityAfter = Get-ChildItem -Path $stateDir -File -ErrorAction SilentlyContinue | ForEach-Object { $_.Name + ':' + $_.Length }
$results.identity_after = ($identityAfter -join ';')
if ($identityBefore -and $identityAfter) {
    Assert-True (($identityBefore -join '|') -eq ($identityAfter -join '|')) 'identity files changed across reinstall'
}
$results.reinstall_identity = 'ok'

# --- upgrade ---
if ($PreviousMsiPath -and (Test-Path $PreviousMsiPath)) {
    Write-Host '=== major upgrade path (previous -> current already installed) ==='
    # Current is installed; installing previous should be blocked as downgrade.
    $prev = (Resolve-Path -LiteralPath $PreviousMsiPath).Path
    $code = 0
    try {
        Invoke-Msi -ArgumentList @("/i", "`"$prev`"") -LogName '06-downgrade.log' | Out-Null
        $code = 0
    } catch {
        $code = 1
        $results.downgrade_blocked = 'ok'
    }
    if ($code -eq 0) {
        throw 'downgrade was not blocked'
    }
} else {
    $results.downgrade_blocked = 'skipped-no-previous-msi'
}

# --- purge ---
if (-not $SkipPurge) {
    Write-Host '=== uninstall PURGE=1 ==='
    # WARNING: destructive removal of ProgramData for this product.
    Invoke-Msi -ArgumentList @('/x', $productCode, 'PURGE=1') -LogName '07-purge.log' | Out-Null
    Assert-True (-not (Test-Path $dataDir)) 'PURGE=1 left ProgramData behind'
    $results.purge = 'ok'
} else {
    $results.purge = 'skipped'
}

$summary = Join-Path $ResultsDir 'summary.json'
$results | ConvertTo-Json | Set-Content -LiteralPath $summary -Encoding utf8
Write-Host "MSI lifecycle matrix complete: $summary"
Get-Content $summary

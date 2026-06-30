#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Installs go-glpi-agent on Windows and registers an hourly Scheduled Task.

.DESCRIPTION
  Copies go-glpi-agent.exe to %ProgramFiles%\go-glpi-agent, seeds agent.cfg under
  %ProgramData%\go-glpi-agent without overwriting an existing config, and creates
  a "go-glpi-agent" Scheduled Task that runs `run` hourly as SYSTEM — the Windows
  analog of the Linux systemd timer. Run from the extracted zip directory.

.PARAMETER IntervalHours
  How often the inventory runs. Default: 1 hour.
#>
param(
    [int]$IntervalHours = 1
)

$ErrorActionPreference = "Stop"

$TaskName  = "go-glpi-agent"
$InstallDir = Join-Path $env:ProgramFiles  "go-glpi-agent"
$DataDir    = Join-Path $env:ProgramData   "go-glpi-agent"
$ExeSrc     = Join-Path $PSScriptRoot "go-glpi-agent.exe"
$CfgSrc     = Join-Path $PSScriptRoot "agent.cfg"
$ExeDst     = Join-Path $InstallDir "go-glpi-agent.exe"
$CfgDst     = Join-Path $DataDir   "agent.cfg"

if (-not (Test-Path $ExeSrc)) { throw "go-glpi-agent.exe not found next to this script." }

Write-Host "Installing binary to $InstallDir ..."
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
New-Item -ItemType Directory -Force -Path $DataDir    | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $DataDir "var") | Out-Null
Copy-Item -Force $ExeSrc $ExeDst

# Seed config only if absent (preserve user edits across upgrades).
if (Test-Path $CfgDst) {
    Write-Host "Keeping existing config at $CfgDst"
} else {
    Copy-Item $CfgSrc $CfgDst
    Write-Host "Wrote default config to $CfgDst — edit the 'server' line before first run."
}

Write-Host "Registering Scheduled Task '$TaskName' (every $IntervalHours h, as SYSTEM) ..."
$action  = New-ScheduledTaskAction -Execute $ExeDst -Argument "run"
# Repeat indefinitely from now, with a randomized 5-minute start delay.
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) `
    -RepetitionInterval (New-TimeSpan -Hours $IntervalHours)
$trigger.RandomDelay = "PT5M"
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
$settings  = New-ScheduledTaskSettingsSet -StartWhenAvailable -DontStopOnIdleEnd `
    -ExecutionTimeLimit (New-TimeSpan -Hours 1)

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
    -Principal $principal -Settings $settings -Force | Out-Null

Write-Host "Running an initial inventory ..."
& $ExeDst run
Write-Host "Done. go-glpi-agent is installed and scheduled."

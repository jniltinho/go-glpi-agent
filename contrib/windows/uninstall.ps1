#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Uninstalls go-glpi-agent: removes the Windows Service and the binary.

.DESCRIPTION
  Stops and removes the "go-glpi-agent" Windows Service (and the legacy
  Scheduled Task from releases <= 0.5.x) and deletes the installed binary. The
  config (agent.cfg next to the binary) and state (deviceid/agentid under
  %ProgramData%) are preserved by default so a later reinstall is not seen as a
  new asset by GLPI. Pass -Purge to delete them.

.PARAMETER Purge
  Also delete agent.cfg and %ProgramData%\go-glpi-agent (deviceid/agentid state).
#>
param(
    [switch]$Purge
)

$ErrorActionPreference = "Stop"

$TaskName   = "go-glpi-agent"
$InstallDir = Join-Path $env:ProgramFiles "go-glpi-agent"
$DataDir    = Join-Path $env:ProgramData  "go-glpi-agent"
$ExeDst     = Join-Path $InstallDir "go-glpi-agent.exe"
$CfgDst     = Join-Path $InstallDir "agent.cfg"

if (Test-Path $ExeDst) {
    Write-Host "Removing Windows Service 'go-glpi-agent' ..."
    & $ExeDst service uninstall
} elseif (Get-Service -Name $TaskName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $TaskName -Force -ErrorAction SilentlyContinue
    sc.exe delete $TaskName | Out-Null
}

# Legacy Scheduled Task from releases <= 0.5.x.
if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Write-Host "Removing legacy Scheduled Task '$TaskName' ..."
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

if (Test-Path $ExeDst) {
    Write-Host "Removing $ExeDst ..."
    Remove-Item -Force $ExeDst
}

if ($Purge) {
    if (Test-Path $InstallDir) {
        Write-Host "Purging $InstallDir (binary folder + config) ..."
        Remove-Item -Recurse -Force $InstallDir
    }
    if (Test-Path $DataDir) {
        Write-Host "Purging $DataDir (state) ..."
        Remove-Item -Recurse -Force $DataDir
    }
} else {
    Write-Host "Kept config at $CfgDst and state in $DataDir (use -Purge to remove)."
}

Write-Host "Done."

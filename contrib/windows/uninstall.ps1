#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Uninstalls go-glpi-agent: removes the Scheduled Task and the binary.

.DESCRIPTION
  Removes the "go-glpi-agent" Scheduled Task and the installed binary. The config
  and state (deviceid/agentid under %ProgramData%) are preserved by default so a
  later reinstall is not seen as a new asset by GLPI. Pass -Purge to delete them.

.PARAMETER Purge
  Also delete %ProgramData%\go-glpi-agent (config + deviceid/agentid state).
#>
param(
    [switch]$Purge
)

$ErrorActionPreference = "Stop"

$TaskName   = "go-glpi-agent"
$InstallDir = Join-Path $env:ProgramFiles "go-glpi-agent"
$DataDir    = Join-Path $env:ProgramData  "go-glpi-agent"

if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Write-Host "Removing Scheduled Task '$TaskName' ..."
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

if (Test-Path $InstallDir) {
    Write-Host "Removing $InstallDir ..."
    Remove-Item -Recurse -Force $InstallDir
}

if ($Purge) {
    if (Test-Path $DataDir) {
        Write-Host "Purging $DataDir (config + state) ..."
        Remove-Item -Recurse -Force $DataDir
    }
} else {
    Write-Host "Kept config + state in $DataDir (use -Purge to remove)."
}

Write-Host "Done."

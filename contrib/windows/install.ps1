#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Installs go-glpi-agent on Windows and registers the Windows Service.

.DESCRIPTION
  Copies go-glpi-agent.exe to %ProgramFiles%\go-glpi-agent, seeds agent.cfg in
  the same folder as the binary without overwriting an existing config, and
  registers the "go-glpi-agent" Windows Service (auto start, runs the daemon
  loop as SYSTEM) — the Windows analog of the Linux systemd unit. Run from the
  extracted zip directory.
#>

$ErrorActionPreference = "Stop"

$InstallDir = Join-Path $env:ProgramFiles "go-glpi-agent"
$ExeSrc     = Join-Path $PSScriptRoot "go-glpi-agent.exe"
$CfgSrc     = Join-Path $PSScriptRoot "agent.cfg"
$ExeDst     = Join-Path $InstallDir "go-glpi-agent.exe"
$CfgDst     = Join-Path $InstallDir "agent.cfg"

if (-not (Test-Path $ExeSrc)) { throw "go-glpi-agent.exe not found next to this script." }

Write-Host "Installing binary to $InstallDir ..."
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Force $ExeSrc $ExeDst

# Seed config only if absent (preserve user edits across upgrades).
if (Test-Path $CfgDst) {
    Write-Host "Keeping existing config at $CfgDst"
} else {
    Copy-Item $CfgSrc $CfgDst
    Write-Host "Wrote default config to $CfgDst — edit the 'server' line before first run."
}

# The exe owns the service lifecycle: removes the legacy Scheduled Task if any,
# registers the Windows Service and starts it.
Write-Host "Registering Windows Service 'go-glpi-agent' ..."
& $ExeDst service install
if ($LASTEXITCODE -ne 0) { throw "service install failed (exit $LASTEXITCODE)" }

Write-Host "Done. go-glpi-agent is installed and running as a Windows Service."

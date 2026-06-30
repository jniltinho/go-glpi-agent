#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Enables the built-in OpenSSH Server on Windows (Server 2019+/Windows 10 1809+).

.DESCRIPTION
  Installs the OpenSSH.Server optional capability, starts and auto-enables the
  sshd service, and opens the firewall on TCP 22. This lets you reach the host
  over SSH (e.g. to run `go-glpi-agent.exe run` or manage the Scheduled Task)
  instead of WinRM. OpenSSH ships with Windows; no third-party software is added.
#>
$ErrorActionPreference = "Stop"

Write-Host "Installing OpenSSH.Server capability ..."
$cap = Get-WindowsCapability -Online | Where-Object Name -like 'OpenSSH.Server*'
if ($cap.State -ne 'Installed') {
    Add-WindowsCapability -Online -Name $cap.Name | Out-Null
}

Write-Host "Starting and enabling the sshd service ..."
Set-Service -Name sshd -StartupType Automatic
Start-Service sshd

# PowerShell as the default shell makes `ssh host powershell-cmd` ergonomic.
New-ItemProperty -Path "HKLM:\SOFTWARE\OpenSSH" -Name DefaultShell `
    -Value "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
    -PropertyType String -Force | Out-Null

if (-not (Get-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -DisplayName 'OpenSSH Server (sshd)' `
        -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22 | Out-Null
}

Write-Host "OpenSSH server is running on TCP 22 (default shell: PowerShell)."

#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
  Full end-to-end matrix on the Windows guest:
  - build MSI if needed
  - install, local inventory, schema validate
  - submit to GLPI 10 and GLPI 11
  - optional reference compare
  - MSI lifecycle (repair / uninstall preserve / reinstall / purge optional)
  - service start/status
  - diagnostics bundle
#>
[CmdletBinding()]
param(
    [string]$Root = 'C:\dotnet-glpi-agent-test',
    [string]$Glpi10 = 'http://10.0.2.2:8180/front/inventory.php',
    [string]$Glpi11 = 'http://10.0.2.2:8181/front/inventory.php',
    [string]$Version = '0.1.0',
    [switch]$SkipPurge,
    [switch]$SkipLifecycle
)

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

$Out = Join-Path $Root 'out'
$Logs = Join-Path $Root 'logs'
$PackageDir = Join-Path $Root 'package'
$Msi = Join-Path $PackageDir 'agent.msi'
$InstallDir = Join-Path ${env:ProgramFiles} 'DotnetGlpiAgent'
$DataDir = Join-Path $env:ProgramData 'DotnetGlpiAgent'
$Cfg = Join-Path $DataDir 'agent.cfg'
$Exe = Join-Path $InstallDir 'dotnet-glpi-agent.exe'
$ServiceName = 'DotnetGlpiAgent'
$Schema10 = Join-Path $Root 'schema\glpi10\inventory.schema.json'
$Schema11 = Join-Path $Root 'schema\glpi11\inventory.schema.json'
$Validator = Join-Path $Root 'tools\glpi-schema-validator\glpi-schema-validator.exe'

$Summary = [ordered]@{
    started_utc = [DateTime]::UtcNow.ToString('o')
    host = $env:COMPUTERNAME
    results = [ordered]@{}
    failures = [System.Collections.Generic.List[string]]::new()
}

function Write-Stage([string]$Name) {
    Write-Host ""
    Write-Host "========== STAGE: $Name =========="
}

function Fail([string]$Message) {
    $Summary.failures.Add($Message) | Out-Null
    Write-Warning $Message
}

function Invoke-MsiQuiet {
    param([string[]]$Args, [string]$LogName)
    $log = Join-Path $Logs $LogName
    $all = @('/qn', '/norestart', '/L*v', $log) + $Args
    $p = Start-Process -FilePath msiexec.exe -ArgumentList $all -Wait -PassThru
    if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) {
        throw "msiexec exit=$($p.ExitCode) log=$log"
    }
    return $p.ExitCode
}

function Test-Endpoint([string]$Url) {
    try {
        $uri = [Uri]$Url
        $tnc = Test-NetConnection -ComputerName $uri.Host -Port $uri.Port -WarningAction SilentlyContinue
        return [bool]$tnc.TcpTestSucceeded
    } catch {
        return $false
    }
}

function Get-FirstJson([string]$Dir) {
    Get-ChildItem -Path $Dir -Filter *.json -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

New-Item -ItemType Directory -Force -Path $Out, $Logs, $PackageDir | Out-Null

try {
    # --- connectivity ---
    Write-Stage 'connectivity'
    $c10 = Test-Endpoint $Glpi10
    $c11 = Test-Endpoint $Glpi11
    $Summary.results.connectivity = @{ glpi10 = $c10; glpi11 = $c11; glpi10_url = $Glpi10; glpi11_url = $Glpi11 }
    if (-not $c10) { Fail "GLPI 10 unreachable: $Glpi10" }
    if (-not $c11) { Fail "GLPI 11 unreachable: $Glpi11" }

    # --- build MSI ---
    Write-Stage 'build-msi'
    if (-not (Test-Path $Msi)) {
        $build = Join-Path $Root 'scripts\Build-MsiOnGuest.ps1'
        & $build -Root $Root -Version $Version *> (Join-Path $Logs 'build-msi.log')
        if (-not (Test-Path $Msi)) { throw "MSI still missing after build; see logs\build-msi.log" }
    }
    $Summary.results.build_msi = @{ path = $Msi; size = (Get-Item $Msi).Length }

    # --- clean install ---
    Write-Stage 'install'
    # Remove any previous product first (best effort)
    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existing) {
        try { Invoke-MsiQuiet -Args @('/x', "`"$Msi`"", 'PURGE=0') -LogName 'pre-uninstall.log' | Out-Null } catch {}
    }
    # STARTSERVICE=0 during msiexec: ServiceControl Start+Wait can hang if the
    # first service start is slow (self-contained .NET cold start under SCM).
    Invoke-MsiQuiet -Args @(
        '/i', "`"$Msi`"",
        "SERVER=$Glpi10",
        'TAG=e2e-dotnet',
        'STARTSERVICE=0',
        'RUNNOW=0'
    ) -LogName 'install.log' | Out-Null

    if (-not (Test-Path $Exe)) { throw 'executable missing after install' }
    if (-not (Test-Path $Cfg)) { throw 'agent.cfg missing after install' }
    $svc = Get-Service -Name $ServiceName
    $Summary.results.install = @{
        service_status = "$($svc.Status)"
        start_type = "$($svc.StartType)"
        exe = $Exe
        cfg = $Cfg
    }
    # Start explicitly after install with a bounded wait.
    try {
        Start-Service -Name $ServiceName -ErrorAction Stop
        $deadline = (Get-Date).AddSeconds(90)
        do {
            Start-Sleep -Seconds 2
            $svc.Refresh()
        } while ($svc.Status -ne 'Running' -and (Get-Date) -lt $deadline)
    } catch {
        Fail "service start: $_"
    }
    $Summary.results.install.service_status_after_start = "$($svc.Status)"

    # --- version / validate-config ---
    Write-Stage 'cli-smoke'
    & $Exe version *> (Join-Path $Logs 'version.log')
    $Summary.results.version_exit = $LASTEXITCODE
    if ($LASTEXITCODE -ne 0) { Fail "version exit=$LASTEXITCODE" }
    & $Exe validate-config --config $Cfg *> (Join-Path $Logs 'validate-config.log')
    $Summary.results.validate_config_exit = $LASTEXITCODE
    if ($LASTEXITCODE -ne 0) { Fail "validate-config exit=$LASTEXITCODE" }

    # --- local inventory ---
    Write-Stage 'local-inventory'
    $localDir = Join-Path $Out 'local'
    New-Item -ItemType Directory -Force -Path $localDir | Out-Null
    & $Exe run --config $Cfg --local $localDir --force --debug *> (Join-Path $Logs 'local-run.log')
    $Summary.results.local_exit = $LASTEXITCODE
    # Exit 4 = partial inventory (some collectors degraded, e.g. battery/monitor
    # WMI classes absent on Server VMs); the produced inventory is still valid.
    if ($LASTEXITCODE -notin 0, 4) { throw "local inventory failed exit=$LASTEXITCODE" }
    $json = Get-FirstJson $localDir
    if (-not $json) { throw 'no local JSON produced' }
    $Summary.results.local_json = $json.FullName
    $Summary.results.local_files = @(Get-ChildItem $localDir -File | Select-Object -ExpandProperty Name)

    # --- schema validation glpi10 + glpi11 ---
    Write-Stage 'schema-validate'
    foreach ($pair in @(
        @{ Name = 'glpi10'; Schema = $Schema10 },
        @{ Name = 'glpi11'; Schema = $Schema11 }
    )) {
        $key = "schema_$($pair.Name)"
        if (-not (Test-Path $pair.Schema)) {
            Fail "schema missing for $($pair.Name)"
            $Summary.results[$key] = 'schema-missing'
            continue
        }
        if (Test-Path $Validator) {
            & $Validator $pair.Schema $json.FullName *> (Join-Path $Logs "schema-$($pair.Name).log")
            $Summary.results[$key] = @{ exit = $LASTEXITCODE; mode = 'validator' }
            if ($LASTEXITCODE -ne 0) { Fail "schema $($pair.Name) failed exit=$LASTEXITCODE" }
        } else {
            $text = Get-Content $json.FullName -Raw
            $ok = $text -match '"deviceid"' -and $text -match '"content"'
            $Summary.results[$key] = @{ exit = $(if ($ok) { 0 } else { 1 }); mode = 'lightweight' }
            if (-not $ok) { Fail "lightweight schema check failed for $($pair.Name)" }
        }
    }

    # --- submit GLPI 10 ---
    Write-Stage 'submit-glpi10'
    & $Exe run --config $Cfg --server $Glpi10 --force --debug --tag e2e-dotnet-g10 *> (Join-Path $Logs 'submit-glpi10.log')
    $Summary.results.submit_glpi10_exit = $LASTEXITCODE
    if ($LASTEXITCODE -notin 0, 4) { Fail "submit GLPI10 exit=$LASTEXITCODE" }

    # --- submit GLPI 11 ---
    Write-Stage 'submit-glpi11'
    & $Exe run --config $Cfg --server $Glpi11 --force --debug --tag e2e-dotnet-g11 *> (Join-Path $Logs 'submit-glpi11.log')
    $Summary.results.submit_glpi11_exit = $LASTEXITCODE
    if ($LASTEXITCODE -notin 0, 4) { Fail "submit GLPI11 exit=$LASTEXITCODE" }

    # --- soft asset assert ---
    Write-Stage 'assert-assets'
    $assert = Join-Path $Root 'scripts\Assert-GlpiAsset.ps1'
    if (Test-Path $assert) {
        & $assert -GlpiServer $Glpi10 -ResultsPath (Join-Path $Out 'glpi10-asset.json') *> (Join-Path $Logs 'assert-glpi10.log')
        & $assert -GlpiServer $Glpi11 -ResultsPath (Join-Path $Out 'glpi11-asset.json') *> (Join-Path $Logs 'assert-glpi11.log')
        $Summary.results.asset_assert = 'ran'
    }

    # --- compare optional ---
    Write-Stage 'compare-refs'
    $compare = Join-Path $Root 'scripts\Compare-Inventories.ps1'
    if (Test-Path $compare) {
        & $compare -OutDir $Out -DotnetLocal $localDir *> (Join-Path $Logs 'compare.log')
        $Summary.results.compare_exit = $LASTEXITCODE
    }

    # --- lifecycle ---
    if (-not $SkipLifecycle) {
        Write-Stage 'msi-lifecycle'
        $life = Join-Path $Root 'scripts\Test-MsiLifecycle.ps1'
        $lifeArgs = @{
            MsiPath = $Msi
            Server = $Glpi10
            Tag = 'e2e-lifecycle'
            ResultsDir = (Join-Path $Out 'msi-lifecycle')
        }
        if ($SkipPurge) { $lifeArgs['SkipPurge'] = $true }
        & $life @lifeArgs *> (Join-Path $Logs 'lifecycle.log')
        $Summary.results.lifecycle_exit = $LASTEXITCODE
        if ($LASTEXITCODE -ne 0) { Fail "lifecycle exit=$LASTEXITCODE" }

        # Reinstall for remaining diagnostics if purge ran
        if (-not (Test-Path $Exe)) {
            Invoke-MsiQuiet -Args @('/i', "`"$Msi`"", "SERVER=$Glpi10", 'TAG=e2e-dotnet', 'STARTSERVICE=0') -LogName 'reinstall-after-life.log' | Out-Null
        }
    } else {
        $Summary.results.lifecycle_exit = 'skipped'
    }

    # --- collect diagnostics ---
    Write-Stage 'collect'
    $bundle = Join-Path $Out 'diagnostics'
    New-Item -ItemType Directory -Force -Path $bundle | Out-Null
    if (Test-Path (Join-Path $DataDir 'logs')) {
        Copy-Item (Join-Path $DataDir 'logs\*') $bundle -Recurse -Force -ErrorAction SilentlyContinue
    }
    Copy-Item (Join-Path $Logs '*') (Join-Path $bundle 'msi-and-run-logs') -Recurse -Force -ErrorAction SilentlyContinue
    try {
        Get-WinEvent -LogName Application -MaxEvents 80 -ErrorAction SilentlyContinue |
            Where-Object { $_.Message -match 'Dotnet|GLPI|inventory' -or $_.ProviderName -match 'Service Control Manager' } |
            Select-Object TimeCreated, Id, ProviderName, Message |
            ConvertTo-Json -Depth 4 |
            Set-Content (Join-Path $bundle 'eventlog.json')
    } catch {}
    Get-Service -Name $ServiceName -ErrorAction SilentlyContinue |
        Select-Object Name, Status, StartType |
        ConvertTo-Json |
        Set-Content (Join-Path $bundle 'service.json')
    $Summary.results.collect = 'ok'

    if ($Summary.failures.Count -eq 0) {
        $Summary.status = 'passed'
    } else {
        $Summary.status = 'failed'
    }
}
catch {
    $Summary.status = 'failed'
    $Summary.error = "$_"
    Fail "$_"
}
finally {
    $Summary.finished_utc = [DateTime]::UtcNow.ToString('o')
    $Summary.failure_count = $Summary.failures.Count
    $path = Join-Path $Out 'summary.json'
    ($Summary | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $path -Encoding utf8
    Write-Host ""
    Write-Host "SUMMARY: $($Summary.status) failures=$($Summary.failures.Count)"
    Write-Host "Wrote $path"
    Get-Content $path
}

if ($Summary.status -ne 'passed') { exit 1 }
exit 0

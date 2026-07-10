#Requires -Version 5.1
<#
  Vagrant provisioner for Dotnet GLPI Agent end-to-end validation.
  Stages (comma-separated via -Stages): install, local, schema, submit, compare, lifecycle, collect
#>
[CmdletBinding()]
param(
    [string]$GlpiServer = 'http://10.0.2.2:8180/front/inventory.php',
    [string]$GlpiVersion = '10',
    [string]$Stages = 'install,local,schema,submit,lifecycle,collect',
    [string]$KeepResources = '0'
)

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

$Root = 'C:\dotnet-glpi-agent-test'
$Package = Join-Path $Root 'package\agent.msi'
$Previous = Join-Path $Root 'package\previous.msi'
$Out = Join-Path $Root 'out'
$Logs = Join-Path $Root 'logs'
$InstallDir = Join-Path ${env:ProgramFiles} 'DotnetGlpiAgent'
$DataDir = Join-Path $env:ProgramData 'DotnetGlpiAgent'
$Cfg = Join-Path $DataDir 'agent.cfg'
$Exe = Join-Path $InstallDir 'dotnet-glpi-agent.exe'
$ServiceName = 'DotnetGlpiAgent'
# Schemas are staged per GLPI version (schema\glpi10\..., schema\glpi11\...).
$Schema = Join-Path $Root "schema\glpi$GlpiVersion\inventory.schema.json"
if (-not (Test-Path $Schema)) {
    $Schema = Join-Path $Root 'schema\inventory.schema.json'
}
$Summary = [ordered]@{
    started_utc = [DateTime]::UtcNow.ToString('o')
    glpi_server = $GlpiServer
    glpi_version = $GlpiVersion
    stages = $Stages
    results = [ordered]@{}
}

function Write-Stage([string]$Name) {
    Write-Host ""
    Write-Host "=== stage: $Name ==="
}

function Save-Summary {
    $Summary.finished_utc = [DateTime]::UtcNow.ToString('o')
    $path = Join-Path $Out 'summary.json'
    ($Summary | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $path -Encoding utf8
    Write-Host "summary: $path"
}

function Test-StageEnabled([string]$Name) {
    $set = $Stages.Split(',') | ForEach-Object { $_.Trim().ToLowerInvariant() }
    return $set -contains $Name.ToLowerInvariant() -or $set -contains 'all'
}

function Invoke-MsiQuiet {
    param([string[]]$Args, [string]$LogName)
    $log = Join-Path $Logs $LogName
    $all = @('/qn', '/norestart', '/L*v', $log) + $Args
    $p = Start-Process -FilePath msiexec.exe -ArgumentList $all -Wait -PassThru
    if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) {
        throw "msiexec exit=$($p.ExitCode) log=$log"
    }
}

New-Item -ItemType Directory -Force -Path $Out, $Logs, (Join-Path $Root 'inventories') | Out-Null

try {
    # Connectivity preflight
    try {
        $uri = [Uri]$GlpiServer
        $tnc = Test-NetConnection -ComputerName $uri.Host -Port $uri.Port -WarningAction SilentlyContinue
        $Summary.results.connectivity = @{
            host = $uri.Host
            port = $uri.Port
            ping = [bool]$tnc.PingSucceeded
            tcp = [bool]$tnc.TcpTestSucceeded
        }
        if (-not $tnc.TcpTestSucceeded) {
            Write-Warning "GLPI endpoint $($uri.Host):$($uri.Port) is not reachable from the guest."
        }
    } catch {
        $Summary.results.connectivity = @{ error = "$_" }
    }

    if (Test-StageEnabled 'install') {
        Write-Stage 'install'
        if (-not (Test-Path $Package)) { throw "MSI not staged at $Package" }
        Invoke-MsiQuiet -Args @(
            '/i', "`"$Package`"",
            "SERVER=$GlpiServer",
            'TAG=vagrant-dotnet',
            'STARTSERVICE=1',
            'RUNNOW=0'
        ) -LogName 'install.log'

        if (-not (Test-Path $Exe)) { throw 'executable missing after install' }
        if (-not (Test-Path $Cfg)) { throw 'agent.cfg missing after install' }

        $acl = icacls.exe $DataDir 2>&1 | Out-String
        $acl | Set-Content (Join-Path $Logs 'acls.txt')
        $svc = Get-Service -Name $ServiceName
        $Summary.results.install = @{
            service_status = $svc.Status.ToString()
            service_start_type = $svc.StartType.ToString()
            install_dir = $InstallDir
            data_dir = $DataDir
        }
        Write-Host "service status=$($svc.Status) start=$($svc.StartType)"
    }

    if (Test-StageEnabled 'local') {
        Write-Stage 'local'
        $localDir = Join-Path $Out 'local'
        New-Item -ItemType Directory -Force -Path $localDir | Out-Null
        & $Exe run --config $Cfg --local $localDir --force --debug *> (Join-Path $Logs 'local-run.log')
        $Summary.results.local = @{
            exit_code = $LASTEXITCODE
            files = @(Get-ChildItem $localDir -File | Select-Object -ExpandProperty Name)
        }
        # Exit 4 = partial inventory (degraded collectors); output is still valid.
        if ($LASTEXITCODE -notin 0, 4) { throw "local inventory failed exit=$LASTEXITCODE" }
    }

    if (Test-StageEnabled 'schema') {
        Write-Stage 'schema'
        $json = Get-ChildItem (Join-Path $Out 'local') -Filter *.json -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $json) {
            # Fallback: agent may write under ProgramData inventories
            $json = Get-ChildItem (Join-Path $DataDir 'inventories') -Filter *.json -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        }
        $validatorDir = Join-Path $Root 'tools\glpi-schema-validator'
        $validator = Get-ChildItem $validatorDir -Filter '*.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($json -and (Test-Path $Schema) -and $validator) {
            # glpi-schema-validator takes two positional arguments: SCHEMA INVENTORY.
            & $validator.FullName $Schema $json.FullName *> (Join-Path $Logs 'schema.log')
            $Summary.results.schema = @{ exit_code = $LASTEXITCODE; inventory = $json.FullName }
            if ($LASTEXITCODE -ne 0) { throw "schema validation failed; see logs\schema.log" }
        } elseif ($json -and (Test-Path $Schema)) {
            # Lightweight presence check when validator binary is absent
            $text = Get-Content $json.FullName -Raw
            if ($text -notmatch '"deviceid"') { throw 'inventory JSON missing deviceid' }
            $Summary.results.schema = @{ exit_code = 0; mode = 'lightweight'; inventory = $json.FullName }
        } else {
            # A missing schema or inventory is a failure, not a silent pass.
            $Summary.results.schema = @{ exit_code = 1; mode = 'skipped'; reason = 'schema or inventory missing' }
            throw 'schema validation could not run: schema or inventory missing'
        }
    }

    if (Test-StageEnabled 'submit') {
        Write-Stage 'submit'
        & $Exe run --config $Cfg --server $GlpiServer --force --debug *> (Join-Path $Logs 'submit.log')
        $Summary.results.submit = @{ exit_code = $LASTEXITCODE }
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "submit exit=$LASTEXITCODE (GLPI may be unreachable)"
        }
        $assert = Join-Path $Root 'scripts\Assert-GlpiAsset.ps1'
        if (Test-Path $assert) {
            & $assert -GlpiServer $GlpiServer -ResultsPath (Join-Path $Out 'glpi-asset.json') *> (Join-Path $Logs 'assert-asset.log')
            $Summary.results.asset_assert = @{ exit_code = $LASTEXITCODE }
        }
    }

    if (Test-StageEnabled 'compare') {
        Write-Stage 'compare'
        $compare = Join-Path $Root 'scripts\Compare-Inventories.ps1'
        if (Test-Path $compare) {
            & $compare -OutDir $Out *> (Join-Path $Logs 'compare.log')
            $Summary.results.compare = @{ exit_code = $LASTEXITCODE }
        }
    }

    if (Test-StageEnabled 'lifecycle') {
        Write-Stage 'lifecycle'
        $life = Join-Path $Root 'scripts\Test-MsiLifecycle.ps1'
        $prevArg = @{}
        if (Test-Path $Previous) { $prevArg['PreviousMsiPath'] = $Previous }
        # Skip purge by default so later collect still has data; set STAGES with purge via full matrix host script.
        & $life -MsiPath $Package -Server $GlpiServer -Tag 'vagrant-lifecycle' -ResultsDir (Join-Path $Out 'msi-lifecycle') -SkipPurge @prevArg `
            *> (Join-Path $Logs 'lifecycle.log')
        $Summary.results.lifecycle = @{ exit_code = $LASTEXITCODE }
        if ($LASTEXITCODE -ne 0) { throw "MSI lifecycle failed exit=$LASTEXITCODE" }
    }

    if (Test-StageEnabled 'collect') {
        Write-Stage 'collect'
        $bundle = Join-Path $Out 'diagnostics'
        New-Item -ItemType Directory -Force -Path $bundle | Out-Null
        if (Test-Path (Join-Path $DataDir 'logs')) {
            Copy-Item (Join-Path $DataDir 'logs\*') $bundle -Recurse -Force -ErrorAction SilentlyContinue
        }
        try {
            Get-WinEvent -LogName Application -MaxEvents 100 -ErrorAction SilentlyContinue |
                Where-Object { $_.ProviderName -match 'Dotnet|GLPI|Service Control Manager' } |
                Select-Object TimeCreated, Id, ProviderName, Message |
                ConvertTo-Json -Depth 4 |
                Set-Content (Join-Path $bundle 'eventlog.json')
        } catch {}
        Get-Service -Name $ServiceName -ErrorAction SilentlyContinue |
            Select-Object Name, Status, StartType |
            ConvertTo-Json |
            Set-Content (Join-Path $bundle 'service.json')
        $Summary.results.collect = 'ok'
    }

    $Summary.status = 'passed'
}
catch {
    $Summary.status = 'failed'
    $Summary.error = "$_"
    Write-Error $_
    exit 1
}
finally {
    Save-Summary
    if ($KeepResources -eq '1') {
        Write-Host 'KEEP_RESOURCES=1: leaving installation and outputs in place.'
    }
}

Write-Host 'Provisioning complete.'
exit 0

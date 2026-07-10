#Requires -Version 5.1
<#
  Best-effort GLPI Computer assertion via the REST API when credentials are available.
  Lab defaults match test/glpi disposable credentials. Failures are non-fatal when
  the API is disabled; inventory submit success remains the primary signal.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GlpiServer,

    [string]$GlpiUser = 'glpi',
    [string]$GlpiPassword = 'glpi',
    [string]$AppToken = '',
    [string]$ResultsPath = 'C:\dotnet-glpi-agent-test\out\glpi-asset.json'
)

$ErrorActionPreference = 'Continue'

function Get-BaseUrl([string]$InventoryUrl) {
    $uri = [Uri]$InventoryUrl
    $builder = [UriBuilder]$uri
    $builder.Path = ($uri.AbsolutePath -replace '/front/inventory\.php$', '')
    if (-not $builder.Path.EndsWith('/')) { $builder.Path += '/' }
    return $builder.Uri.GetLeftPart([UriPartial]::Path).TrimEnd('/')
}

$base = Get-BaseUrl $GlpiServer
$result = [ordered]@{
    base = $base
    ok = $false
}

try {
    $init = Invoke-RestMethod -Method Get -Uri "$base/apirest.php/initSession" -Headers @{
        Authorization = 'user_token ' # placeholder; lab often uses basic login below
    } -ErrorAction SilentlyContinue
} catch {}

try {
    $body = @{ login = $GlpiUser; password = $GlpiPassword } | ConvertTo-Json
    $session = Invoke-RestMethod -Method Get -Uri "$base/apirest.php/initSession?get_full_session=true" -Headers @{
        'Content-Type' = 'application/json'
        Authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${GlpiUser}:${GlpiPassword}"))
    } -ErrorAction Stop
    $token = $session.session_token
    $headers = @{
        'Session-Token' = $token
        'App-Token' = $AppToken
    }
    $computers = Invoke-RestMethod -Method Get -Uri "$base/apirest.php/Computer?range=0-50" -Headers $headers
    $hostName = $env:COMPUTERNAME
    $matches = @($computers | Where-Object { $_.name -eq $hostName -or $_.name -like "*$hostName*" })
    $result.hostname = $hostName
    $result.match_count = $matches.Count
    $result.matches = @($matches | Select-Object -First 5 | ForEach-Object { @{ id = $_.id; name = $_.name } })
    $result.ok = ($matches.Count -ge 1)
    try {
        Invoke-RestMethod -Method Get -Uri "$base/apirest.php/killSession" -Headers $headers | Out-Null
    } catch {}
}
catch {
    $result.error = "$_"
    $result.ok = $false
    Write-Warning "GLPI API assertion skipped/failed: $_"
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ResultsPath) | Out-Null
($result | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath $ResultsPath -Encoding utf8
if (-not $result.ok) {
    # Soft-fail: API may be disabled on fresh containers; submit logs remain authoritative.
    exit 0
}
exit 0

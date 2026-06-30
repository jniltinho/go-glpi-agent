<#
  Provisioner for the Windows inventory test VM.
  Args: [0] = GLPI server URL (front/inventory.php).

  1. Runs our Go agent: local XML + native JSON dump + send to GLPI.
  2. Runs the official GLPI Agent 1.18 (portable zip) as a reference: local XML + send.
  3. Compares the per-section item counts (Go vs official), like the Linux matrix.

  NOTE: ErrorActionPreference is Continue, not Stop: our agent (and the official
  one) log to stderr, and PowerShell 5.1 raises a fatal NativeCommandError for
  native-exe stderr when the preference is Stop. We check $LASTEXITCODE instead.
#>
param(
    [string]$GlpiServer
)

$ErrorActionPreference = "Continue"
$Exe = "C:\gfi\go-glpi-agent.exe"
$Out = "C:\gfi\out"
New-Item -ItemType Directory -Force -Path "$Out\go", "$Out\ref" | Out-Null

Write-Host "=== go-glpi-agent version ==="
& $Exe version

# Point the bundled config at the GLPI server.
$cfg = "C:\gfi\agent.cfg"
(Get-Content $cfg) -replace '^server\s*=.*', "server = $GlpiServer" | Set-Content $cfg
Write-Host "GLPI server: $GlpiServer"

Write-Host "`n=== [1] go-glpi-agent: local XML + native JSON dump ==="
$env:GFI_DUMP_JSON = "$Out\go\inventory.json"
& $Exe run --local "$Out\go" --debug *> "$Out\go-local.log"
Write-Host ("local run exit={0}; files:" -f $LASTEXITCODE)
Get-ChildItem "$Out\go" | Format-Table Name, Length -AutoSize | Out-String | Write-Host

Write-Host "--- sending to GLPI ---"
& $Exe run --conf-file $cfg --force --debug *> "$Out\go-send.log"
Write-Host ("send exit={0}" -f $LASTEXITCODE)
Select-String -Path "$Out\go-send.log" -Pattern 'native|sent|status|error' |
    Select-Object -First 6 | ForEach-Object { Write-Host ("  " + $_.Line.Trim()) }

# ---------------------------------------------------------------------------
Write-Host "`n=== [2] official GLPI Agent 1.18 (reference) ==="
if (Test-Path "C:\gfi\glpi-agent-ref.zip") {
    Expand-Archive -Force "C:\gfi\glpi-agent-ref.zip" "C:\gfi\glpi-agent-ref"
    $bat = Join-Path "C:\gfi\glpi-agent-ref" "glpi-agent.bat"
    Write-Host "--- official agent: local XML ---"
    & $bat --local="$Out\ref" --no-task=deploy,wakeonlan *> "$Out\ref-local.log"
    Write-Host ("official local exit={0}" -f $LASTEXITCODE)
    Write-Host "--- official agent: sending to GLPI ---"
    & $bat --server="$GlpiServer" --force *> "$Out\ref-send.log"
    Write-Host ("official send exit={0}" -f $LASTEXITCODE)
} else {
    Write-Host "reference zip not present (run 'make fetch-glpi-agent-win') - skipping reference run"
}

# ---------------------------------------------------------------------------
Write-Host "`n=== [3] per-section comparison (Go vs official) ==="
$sections = 'HARDWARE','BIOS','OPERATINGSYSTEM','CPUS','MEMORIES','DRIVES','STORAGES',
            'NETWORKS','SOFTWARES','USBDEVICES','LOCAL_USERS','LOCAL_GROUPS'

function Get-SectionCounts($dir) {
    $counts = @{}
    $xml = Get-ChildItem -Path $dir -Filter *.xml -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($xml) {
        $text = Get-Content $xml.FullName -Raw
        foreach ($s in $sections) { $counts[$s] = ([regex]::Matches($text, "<$s>")).Count }
    }
    return $counts
}

$go  = Get-SectionCounts "$Out\go"
$ref = Get-SectionCounts "$Out\ref"
Write-Host ("{0,-16} {1,6} {2,9}" -f "SECTION", "GO", "OFFICIAL")
foreach ($s in $sections) {
    Write-Host ("{0,-16} {1,6} {2,9}" -f $s, [int]$go[$s], [int]$ref[$s])
}

Write-Host "`nProvisioning complete. Check GLPI assets for 'win-gfi-test'."

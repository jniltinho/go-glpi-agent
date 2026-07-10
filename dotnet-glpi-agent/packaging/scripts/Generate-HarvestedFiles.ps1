#Requires -Version 5.1
<#
  Generate HarvestedFiles.wxs listing every published payload file except the main
  executable (owned by the service component) and PDBs.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$PublishDir = (Resolve-Path -LiteralPath $PublishDir).Path.TrimEnd('\', '/')

$files = Get-ChildItem -Path $PublishDir -File -Recurse | Where-Object {
    $_.Name -ne 'dotnet-glpi-agent.exe' -and $_.Extension -ne '.pdb'
} | Sort-Object FullName

$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine('    <ComponentGroup Id="PublishedAgentFiles" Directory="INSTALLFOLDER">')

$md5 = [System.Security.Cryptography.MD5]::Create()

$index = 0
foreach ($file in $files) {
    $relative = $file.FullName.Substring($PublishDir.Length).TrimStart('\', '/')
    $relativeUnix = $relative.Replace('\', '/')
    $id = 'Pub' + ($index.ToString('D4'))
    # Deterministic per-path GUID: MSI component rules require the same file to
    # keep the same component GUID across rebuilds of the same product.
    $hash = $md5.ComputeHash([Text.Encoding]::UTF8.GetBytes('DotnetGlpiAgent.Harvest:' + $relativeUnix.ToLowerInvariant()))
    $guid = [guid]::new($hash).ToString('B').ToUpperInvariant()
    $source = '$(var.PublishDir)\' + $relative.Replace('/', '\')

    # Flatten into INSTALLFOLDER; self-contained publish is a single directory.
    if ($relativeUnix.Contains('/')) {
        # Nested path (rare for self-contained): put under INSTALLFOLDER preserving name only.
        $name = [IO.Path]::GetFileName($relativeUnix)
        [void]$sb.AppendLine("      <Component Id=`"$id`" Guid=`"$guid`" Bitness=`"always64`">")
        [void]$sb.AppendLine("        <File Id=`"${id}File`" Source=`"$source`" Name=`"$name`" KeyPath=`"yes`" Checksum=`"yes`" />")
        [void]$sb.AppendLine('      </Component>')
    } else {
        [void]$sb.AppendLine("      <Component Id=`"$id`" Guid=`"$guid`" Bitness=`"always64`">")
        [void]$sb.AppendLine("        <File Id=`"${id}File`" Source=`"$source`" KeyPath=`"yes`" Checksum=`"yes`" />")
        [void]$sb.AppendLine('      </Component>')
    }
    $index++
}

[void]$sb.AppendLine('    </ComponentGroup>')
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('</Wix>')

$dir = Split-Path -Parent $OutputPath
if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
[IO.File]::WriteAllText($OutputPath, $sb.ToString(), [Text.UTF8Encoding]::new($false))
Write-Host "Harvested $index payload files into $OutputPath"

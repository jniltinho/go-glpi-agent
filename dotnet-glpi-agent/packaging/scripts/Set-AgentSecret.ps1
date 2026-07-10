[CmdletBinding(DefaultParameterSetName = 'Prompt')]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('password', 'proxy-password', 'client-cert-password')]
    [string]$Name,

    [Parameter(ParameterSetName = 'File', Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$SecretFile,

    [string]$Configuration = "$env:ProgramData\DotnetGlpiAgent\agent.cfg"
)

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Secret provisioning requires an elevated administrator session.'
}

if ($PSCmdlet.ParameterSetName -eq 'File') {
    $acl = Get-Acl -LiteralPath $SecretFile
    $unsafe = $acl.Access | Where-Object {
        $_.AccessControlType -eq 'Allow' -and
        $_.IdentityReference.Value -match 'Everyone|Authenticated Users|BUILTIN\\Users' -and
        $_.FileSystemRights.ToString() -match 'Write|Modify|FullControl'
    }
    if ($unsafe) {
        throw 'The secret file grants write access to an unprivileged principal.'
    }
    $secret = (Get-Content -LiteralPath $SecretFile -Raw).TrimEnd("`r", "`n")
} else {
    $secure = Read-Host "Enter $Name" -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        $secret = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    } finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

if ([string]::IsNullOrEmpty($secret)) {
    throw 'The secret must not be empty.'
}

$lines = if (Test-Path -LiteralPath $Configuration) {
    [Collections.Generic.List[string]](Get-Content -LiteralPath $Configuration)
} else {
    [Collections.Generic.List[string]]::new()
}
$replacement = "$Name = $secret"
$index = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "^\s*$([regex]::Escape($Name))\s*=") {
        $index = $i
        break
    }
}
if ($index -ge 0) {
    $lines[$index] = $replacement
} else {
    $lines.Add($replacement)
}

$temporary = "$Configuration.$([Guid]::NewGuid().ToString('N')).tmp"
try {
    [IO.File]::WriteAllLines($temporary, $lines, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $Configuration -Force
    & icacls.exe $Configuration /inheritance:r /grant:r '*S-1-5-18:(F)' '*S-1-5-32-544:(F)' | Out-Null
} finally {
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    $secret = $null
}

if ($PSCmdlet.ParameterSetName -eq 'File') {
    Remove-Item -LiteralPath $SecretFile -Force
}

Restart-Service -Name DotnetGlpiAgent -ErrorAction SilentlyContinue

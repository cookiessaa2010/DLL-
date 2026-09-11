[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$EvidenceDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path -LiteralPath $EvidenceDirectory -PathType Container)) {
    throw "KaiTOR TOR post-battle evidence directory not found: $EvidenceDirectory"
}

$root = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
$manifestPath = Join-Path $root 'acceptance-post-battle.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "KaiTOR TOR post-battle acceptance manifest not found: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schema -ne 'kaitor-online-tor-post-battle-live-acceptance/v1') {
    throw "Unsupported KaiTOR TOR post-battle acceptance manifest schema: $($manifest.schema)"
}
if ($manifest.result -ne 'PASS') {
    throw "KaiTOR TOR post-battle acceptance manifest result is not PASS: $($manifest.result)"
}
if ($manifest.target.bannerlord -ne '1.3.15.110062') {
    throw "Unexpected Bannerlord target in TOR post-battle manifest: $($manifest.target.bannerlord)"
}
if ($manifest.target.theOldRealms -ne '1.3.15') {
    throw "Unexpected The Old Realms target in TOR post-battle manifest: $($manifest.target.theOldRealms)"
}
if ([int]$manifest.target.simultaneousPlayers -ne 4) {
    throw "Unexpected simultaneous-player contract in TOR post-battle manifest: $($manifest.target.simultaneousPlayers)"
}
if ($manifest.target.campaignProcess -ne 'Bannerlord.exe /singleplayer /server') {
    throw "Unexpected campaign process in TOR post-battle manifest: $($manifest.target.campaignProcess)"
}
if ([string]::IsNullOrWhiteSpace([string]$manifest.target.controllerId)) {
    throw 'TOR post-battle manifest controllerId is empty.'
}

$requiredEvidence = @(
    'runtimeLog',
    'inBattleCommandOutput',
    'postBattleCommandOutput',
    'movedCommandOutput',
    'inBattleSnapshot',
    'postBattleSnapshot',
    'movedSnapshot'
)

$seenFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($property in $requiredEvidence) {
    $entry = $manifest.evidence.$property
    if ($null -eq $entry) {
        throw "TOR post-battle acceptance manifest missing evidence entry: $property"
    }

    $fileName = [string]$entry.file
    if ([string]::IsNullOrWhiteSpace($fileName) -or $fileName -ne [IO.Path]::GetFileName($fileName)) {
        throw "Unsafe TOR post-battle evidence filename for ${property}: $fileName"
    }
    if (-not $seenFiles.Add($fileName)) {
        throw "Duplicate TOR post-battle evidence filename in manifest: $fileName"
    }

    $path = Join-Path $root $fileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "TOR post-battle evidence file missing for ${property}: $path"
    }

    $item = Get-Item -LiteralPath $path
    if ($item.Length -ne [long]$entry.bytes) {
        throw "TOR post-battle evidence length mismatch for $property ($fileName)."
    }

    $expectedHash = ([string]$entry.sha256).ToLowerInvariant()
    if ($expectedHash -notmatch '^[0-9a-f]{64}$') {
        throw "Invalid TOR post-battle evidence SHA-256 in manifest for $property ($fileName)."
    }
    $actualHash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "TOR post-battle evidence SHA-256 mismatch for $property ($fileName)."
    }
}

Write-Output "KaiTOR TOR post-battle live evidence verified: $root"
Write-Output 'KaiTOR Online TOR post-battle live evidence: PASS'

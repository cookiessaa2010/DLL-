[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$EvidenceDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path -LiteralPath $EvidenceDirectory -PathType Container)) {
    throw "KaiTOR evidence directory not found: $EvidenceDirectory"
}

$root = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
$manifestPath = Join-Path $root 'acceptance.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "KaiTOR acceptance manifest not found: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schema -ne 'kaitor-online-live-acceptance/v1') {
    throw "Unsupported KaiTOR acceptance manifest schema: $($manifest.schema)"
}
if ($manifest.result -ne 'PASS') {
    throw "KaiTOR acceptance manifest result is not PASS: $($manifest.result)"
}
if ($manifest.target.bannerlord -ne '1.3.15.110062') {
    throw "Unexpected Bannerlord target in acceptance manifest: $($manifest.target.bannerlord)"
}
if ($manifest.target.theOldRealms -ne '1.3.15') {
    throw "Unexpected The Old Realms target in acceptance manifest: $($manifest.target.theOldRealms)"
}
if ([int]$manifest.target.simultaneousPlayers -ne 4) {
    throw "Unexpected simultaneous-player contract in acceptance manifest: $($manifest.target.simultaneousPlayers)"
}
if ($manifest.target.campaignProcess -ne 'Bannerlord.exe /singleplayer /server') {
    throw "Unexpected campaign process in acceptance manifest: $($manifest.target.campaignProcess)"
}

$requiredEvidence = @('runtimeLog','beforeCommandOutput','afterCommandOutput','beforeSnapshot','afterSnapshot')
foreach ($property in $requiredEvidence) {
    $entry = $manifest.evidence.$property
    if ($null -eq $entry) {
        throw "Acceptance manifest missing evidence entry: $property"
    }

    $fileName = [string]$entry.file
    if ([string]::IsNullOrWhiteSpace($fileName) -or $fileName -ne [IO.Path]::GetFileName($fileName)) {
        throw "Unsafe evidence filename in acceptance manifest for ${property}: $fileName"
    }

    $path = Join-Path $root $fileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Acceptance evidence file missing for ${property}: $path"
    }

    $item = Get-Item -LiteralPath $path
    if ($item.Length -ne [long]$entry.bytes) {
        throw "Acceptance evidence length mismatch for $property ($fileName)."
    }

    $actualHash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $expectedHash = ([string]$entry.sha256).ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "Acceptance evidence SHA-256 mismatch for $property ($fileName)."
    }
}

Write-Output "KaiTOR live acceptance evidence verified: $root"
Write-Output 'KaiTOR Online live acceptance evidence: PASS'

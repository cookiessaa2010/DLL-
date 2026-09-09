[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$InputPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputPath,

    [ValidateSet('four-online', 'slot-freed')]
    [string]$Phase = 'four-online',

    [switch]$SkipValidation
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path -LiteralPath $InputPath -PathType Leaf)) {
    throw "KaiTOR command output not found: $InputPath"
}

$marker = 'KAITOR_4P_SNAPSHOT_JSON='
$matches = @(Get-Content -LiteralPath $InputPath | Where-Object { $_ -like "*$marker*" })
if ($matches.Count -eq 0) {
    throw "No '$marker' record found in $InputPath. Run coop.debug.kaitor.snapshot4p on the authoritative server first."
}

$line = [string]$matches[-1]
$markerIndex = $line.IndexOf($marker, [StringComparison]::Ordinal)
if ($markerIndex -lt 0) {
    throw 'Snapshot marker was selected but could not be located.'
}

$json = $line.Substring($markerIndex + $marker.Length).Trim()
try {
    $parsed = $json | ConvertFrom-Json
}
catch {
    throw "KaiTOR snapshot payload is invalid JSON: $($_.Exception.Message)"
}

if ($null -eq $parsed.players) {
    throw "KaiTOR snapshot payload does not contain a players array."
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$normalized = $parsed | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText($OutputPath, $normalized + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Output "KaiTOR runtime snapshot exported: $OutputPath"

if (-not $SkipValidation) {
    $validator = Join-Path $PSScriptRoot 'Test-KaiTORFourPlayerSnapshot.ps1'
    if (-not (Test-Path -LiteralPath $validator -PathType Leaf)) {
        throw "KaiTOR snapshot validator not found: $validator"
    }

    & $validator -SnapshotPath $OutputPath -Phase $Phase
    if ($LASTEXITCODE -ne 0) {
        throw "KaiTOR four-player snapshot validation failed with exit code $LASTEXITCODE."
    }
}

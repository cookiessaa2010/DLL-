[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$LogPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BeforeSnapshotPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$AfterSnapshotPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runtimeDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$torLogValidator = Join-Path $runtimeDir 'Test-KaiTORTorRuntimeLog.ps1'
$snapshotValidator = Join-Path $runtimeDir 'Test-KaiTORFourPlayerSnapshot.ps1'
$movementValidator = Join-Path $runtimeDir 'Test-KaiTORFourPlayerMovement.ps1'

foreach ($requiredScript in @($torLogValidator, $snapshotValidator, $movementValidator)) {
    if (-not (Test-Path -LiteralPath $requiredScript -PathType Leaf)) {
        throw "KaiTOR runtime validator missing: $requiredScript"
    }
}

Write-Output '=== TOR + Coop runtime evidence ==='
& $torLogValidator -LogPath $LogPath
if ($LASTEXITCODE -ne 0) {
    throw "TOR runtime log validation failed with exit code $LASTEXITCODE"
}

Write-Output '=== Four-player snapshot: before movement ==='
& $snapshotValidator -SnapshotPath $BeforeSnapshotPath
if ($LASTEXITCODE -ne 0) {
    throw "Before snapshot validation failed with exit code $LASTEXITCODE"
}

Write-Output '=== Four-player snapshot: after movement ==='
& $snapshotValidator -SnapshotPath $AfterSnapshotPath
if ($LASTEXITCODE -ne 0) {
    throw "After snapshot validation failed with exit code $LASTEXITCODE"
}

Write-Output '=== Four-player authoritative movement ==='
& $movementValidator -BeforeSnapshotPath $BeforeSnapshotPath -AfterSnapshotPath $AfterSnapshotPath
if ($LASTEXITCODE -ne 0) {
    throw "Four-player movement validation failed with exit code $LASTEXITCODE"
}

Write-Output 'KaiTOR Online TOR 4-player runtime acceptance: PASS'

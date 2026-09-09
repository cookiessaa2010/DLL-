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

# These are PowerShell script invocations, not native processes. With ErrorActionPreference=Stop,
# any rejection throws immediately; do not inspect $LASTEXITCODE because it is undefined/stale here.
Write-Output '=== TOR + Coop runtime evidence ==='
& $torLogValidator -LogPath $LogPath

Write-Output '=== Four-player snapshot: before movement ==='
& $snapshotValidator -SnapshotPath $BeforeSnapshotPath

Write-Output '=== Four-player snapshot: after movement ==='
& $snapshotValidator -SnapshotPath $AfterSnapshotPath

Write-Output '=== Four-player authoritative movement ==='
& $movementValidator -BeforeSnapshotPath $BeforeSnapshotPath -AfterSnapshotPath $AfterSnapshotPath

Write-Output 'KaiTOR Online TOR 4-player runtime acceptance: PASS'

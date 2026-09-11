[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$LogPath,
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$InBattleSnapshotPath,
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$PostBattleSnapshotPath,
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$MovedSnapshotPath,
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$ControllerId,
    [ValidateRange(0.000001,1000.0)][double]$MinimumDistance = 0.01
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runtimeDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$torLogValidator = Join-Path $runtimeDir 'Test-KaiTORTorRuntimeLog.ps1'
$snapshotValidator = Join-Path $runtimeDir 'Test-KaiTORFourPlayerSnapshot.ps1'
$postBattleValidator = Join-Path $runtimeDir 'Test-KaiTORFourPlayerPostBattleMovement.ps1'

foreach ($requiredScript in @($torLogValidator, $snapshotValidator, $postBattleValidator)) {
    if (-not (Test-Path -LiteralPath $requiredScript -PathType Leaf)) {
        throw "KaiTOR TOR post-battle validator dependency missing: $requiredScript"
    }
}

Write-Output '=== TOR + Coop runtime evidence ==='
& $torLogValidator -LogPath $LogPath

foreach ($snapshot in @(
    @{ Name = 'in-battle'; Path = $InBattleSnapshotPath },
    @{ Name = 'post-battle'; Path = $PostBattleSnapshotPath },
    @{ Name = 'moved'; Path = $MovedSnapshotPath }
)) {
    Write-Output "=== Four-player snapshot: $($snapshot.Name) ==="
    & $snapshotValidator -SnapshotPath $snapshot.Path -Phase four-online
}

Write-Output '=== TOR four-player post-battle authoritative movement ==='
& $postBattleValidator `
    -InBattleSnapshotPath $InBattleSnapshotPath `
    -PostBattleSnapshotPath $PostBattleSnapshotPath `
    -MovedSnapshotPath $MovedSnapshotPath `
    -ControllerId $ControllerId `
    -MinimumDistance $MinimumDistance

Write-Output 'KaiTOR Online TOR post-battle 4-player runtime acceptance: PASS'

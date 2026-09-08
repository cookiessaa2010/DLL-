param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'

function Replace-Exact {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Old,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$New
    )

    $fullPath = Join-Path $UpstreamRoot $Path
    if (-not (Test-Path $fullPath)) {
        throw "Missing upstream file: $Path"
    }

    $text = [IO.File]::ReadAllText($fullPath) -replace "`r`n", "`n"
    $oldNormalized = $Old -replace "`r`n", "`n"
    $newNormalized = $New -replace "`r`n", "`n"

    if (-not $text.Contains($oldNormalized)) {
        throw "Anchor not found in $Path`n--- anchor ---`n$oldNormalized"
    }

    [IO.File]::WriteAllText(
        $fullPath,
        $text.Replace($oldNormalized, $newNormalized),
        [Text.UTF8Encoding]::new($false))
}

# Bannerlord 1.3.15 exposes the spawn phase as the nested
# MissionAgentSpawnLogic.SpawnPhase type. Later versions renamed/extracted it to MissionSpawnPhase.
# The fields used by CoopBattleMissionSpawnHandler are present on the 1.3.15 nested type.
Replace-Exact `
    'source/Missions/Battles/CoopBattleMissionSpawnHandler.cs' `
    '    internal static void ReconcilePhaseLifetimeQuota(MissionSpawnPhase phase, int refreshedOwnedTarget,' `
    '    internal static void ReconcilePhaseLifetimeQuota(MissionAgentSpawnLogic.SpawnPhase phase, int refreshedOwnedTarget,'

Write-Host 'Applied KaiTOR Bannerlord 1.3.15 Missions backport.'

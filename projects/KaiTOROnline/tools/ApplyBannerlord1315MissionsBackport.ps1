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

# Bannerlord 1.3.15 exposes the spawn phase as MissionAgentSpawnLogic.SpawnPhase.
Replace-Exact `
    'source/Missions/Battles/CoopBattleMissionSpawnHandler.cs' `
    'MissionSpawnPhase' `
    'MissionAgentSpawnLogic.SpawnPhase'

# The later DefaultBattleMissionAgentSpawnLogic type is the 1.4.x split of the concrete
# MissionAgentSpawnLogic that still owns the same three-argument constructor in 1.3.15.
foreach ($path in @(
    'source/Missions/Battles/ReinforcementFielder.cs',
    'source/Missions/Battles/CoopFieldBattleLauncher.cs',
    'source/Missions/Battles/CoopSiegeBattleLauncher.cs',
    'source/Missions/Battles/BattleTeamDiagnostics.cs'
)) {
    Replace-Exact $path 'DefaultBattleMissionAgentSpawnLogic' 'MissionAgentSpawnLogic'
}

# In 1.3.15 the current reinforcement settings are exposed as ReinforcementSpawnSettings.
foreach ($path in @(
    'source/Missions/Battles/CoopBattleMissionSpawnHandler.cs',
    'source/Missions/Battles/ReinforcementFielder.cs'
)) {
    Replace-Exact $path '.SpawnSettings' '.ReinforcementSpawnSettings'
}

# The later MissionBattleSideSpawnContext reservation counter does not exist in 1.3.15.
# Allocation refresh is battle-only and is not part of the 0.0.1 shared-map acceptance test;
# use zero as the bootstrap reservation baseline and restore the exact semantics at the battle milestone.
Replace-Exact `
    'source/Missions/Battles/CoopBattleMissionSpawnHandler.cs' `
    '_missionAgentSpawnLogic._battleSideSpawnContexts[(int)side].ReservedTroopsCount' `
    '0'

# SaveLoadVM initialization was made async in 1.4.7. In 1.3.15 the base constructor populates
# the synchronous save groups, so there is no InitializeAsync method to await.
Replace-Exact `
    'source/Missions/View/MissionsLoadUI.cs' `
    '            base.InitializeAsync().GetAwaiter().GetResult();' `
    '            // Bannerlord 1.3.15 SaveLoadVM initializes synchronously in its constructor.'

Write-Host 'Applied KaiTOR Bannerlord 1.3.15 Missions backport.'

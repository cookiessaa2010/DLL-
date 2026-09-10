param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'

function Replace-Exact {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Old,
        [Parameter(Mandatory = $true)][string]$New
    )

    $fullPath = Join-Path $UpstreamRoot $Path
    if (-not (Test-Path -LiteralPath $fullPath)) {
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

$projectRoot = Split-Path -Parent $PSScriptRoot
$overlay = Join-Path $projectRoot 'upstream-overlay/source/Coop.Core/Server/Commands/KaiTORFourPlayerSnapshotCommand.cs'
$target = Join-Path $UpstreamRoot 'source/Coop.Core/Server/Commands/KaiTORFourPlayerSnapshotCommand.cs'

if (-not (Test-Path -LiteralPath $overlay)) {
    throw "Missing KaiTOR shared-map diagnostics overlay: $overlay"
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
Copy-Item -LiteralPath $overlay -Destination $target -Force

Replace-Exact `
    'source/Coop.Core/Server/ServerModule.cs' `
    'using Coop.Core.Server.Connections;' `
    "using Coop.Core.Server.Connections;`nusing Coop.Core.Server.Commands;"

Replace-Exact `
    'source/Coop.Core/Server/ServerModule.cs' `
    '        builder.RegisterModule<ConnectionModule>();' `
    "        builder.RegisterModule<ConnectionModule>();`n        builder.RegisterType<KaiTORFourPlayerSnapshotCommand>().As<ICoopCommand>().InstancePerDependency();"

# Shared-map diagnostics are only useful if each remote controller is authoritative over its own
# party and cannot submit behavior for another player's MobileParty. Keep this core gate in the
# same full-build staging path so every packaged Coop.Core carries it.
& (Join-Path $PSScriptRoot 'ApplyFourPlayerMovementOwnership.ps1') -UpstreamRoot $UpstreamRoot

# Encounter/conversation ids are client supplied too. Bind every request to the authenticated
# peer's persistent MobileParty before vanilla PlayerEncounter/MapEvent creation can run.
& (Join-Path $PSScriptRoot 'ApplyFourPlayerEncounterOwnership.ps1') -UpstreamRoot $UpstreamRoot

# Mission teardown is also a client-originated command. Require the requesting peer to resolve to
# a registered player whose authoritative MobileParty is part of the exact MapEvent before a live
# battle can be finalized. The 1.3.15 implementation proves membership through InvolvedParties.
& (Join-Path $PSScriptRoot 'ApplyFourPlayerMissionFinalizeOwnership.ps1') -UpstreamRoot $UpstreamRoot

# Leaving a still-live battle is a separate client-originated path from finalization. Reject a
# NetworkRequestLeaveBattle unless its NetPeer owns the exact PartyId being removed, otherwise one
# campaign-map client could eject another player's party from an active mission/MapEvent.
& (Join-Path $PSScriptRoot 'ApplyFourPlayerBattleLeaveOwnership.ps1') -UpstreamRoot $UpstreamRoot

# Campaign time is a global authoritative resource. A stale or unknown NetPeer must never be able
# to pause/unpause/fast-forward the shared campaign; only a currently registered controller may
# request a mode change. Existing occupancy policies still decide the effective speed.
& (Join-Path $PSScriptRoot 'ApplyFourPlayerTimeAuthority.ps1') -UpstreamRoot $UpstreamRoot

# A defeated player's MobileParty may legitimately disappear, but reconnect/restart must not
# fabricate a new active party around a permanently dead Hero. Successor/respawn policy is a
# separate explicit transition. Prisoner restoration remains parked/inactive in upstream logic.
& (Join-Path $PSScriptRoot 'ApplyFourPlayerDefeatLifecycleSafety.ps1') -UpstreamRoot $UpstreamRoot

# Fail staging before compilation if either edge of the partial-battle contract regresses: mission
# start must stay participant-targeted, battle leave must stay owned, and finalization must stay
# peer/player/party/event-owned.
& (Join-Path $PSScriptRoot 'TestFourPlayerMissionIsolationStaging.ps1') -UpstreamRoot $UpstreamRoot

# Verify the global time resource remains server-authoritative while preserving the upstream
# partial-battle rule: auto-pause only when every connected player is occupied.
& (Join-Path $PSScriptRoot 'TestFourPlayerTimeAuthorityStaging.ps1') -UpstreamRoot $UpstreamRoot

Write-Host 'KaiTOR shared-map diagnostics applied successfully.'
Write-Host 'Server command: coop.debug.kaitor.snapshot4p'

param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'

function Read-UpstreamText {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $path = Join-Path $UpstreamRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing upstream file: $RelativePath"
    }

    return [IO.File]::ReadAllText($path) -replace "`r`n", "`n"
}

function Require-Text {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Needle,
        [Parameter(Mandatory = $true)][string]$Failure
    )

    if (-not $Text.Contains($Needle)) {
        throw $Failure
    }
}

$missionStart = Read-UpstreamText 'source/GameInterface/Services/MapEvents/Handlers/BattleMissionStartHandler.cs'
$joinLeave = Read-UpstreamText 'source/GameInterface/Services/MapEvents/Handlers/BattleJoinLeaveHandler.cs'
$finalize = Read-UpstreamText 'source/GameInterface/Services/MapEvents/Handlers/BattleFinalizeHandler.cs'

# Enter isolation: the server derives recipients from authoritative MapEvent membership and sends
# the mission-open message only to those peers. Players C/D who are not in A/B's MapEvent stay on map.
Require-Text $missionStart `
    'mapEvent.FindMapEventParty(party.Party, out var side) is not MapEventParty mapEventParty' `
    'Mission participants are not derived from authoritative MapEvent membership.'
Require-Text $missionStart `
    'foreach (var participant in participants)' `
    'Mission start no longer iterates the authoritative participant set.'
Require-Text $missionStart `
    'network.Send(participant.Peer, message);' `
    'Mission start is not targeted to individual authoritative participants.'

if ($missionStart.Contains('network.SendAll(missionStartMessage)') -or
    $missionStart.Contains('network.SendAll(message);')) {
    throw 'Mission start contains a global broadcast path that can drag non-participants into battle.'
}

# Mid-battle leave isolation: client-supplied PartyId must be owned by the exact requesting NetPeer
# before the server calls the authoritative removal path. This prevents C/D from ejecting A/B.
Require-Text $joinLeave `
    'var requestingPeer = payload.Who as NetPeer;' `
    'Battle leave does not resolve the requesting NetPeer.'
Require-Text $joinLeave `
    'if (requestingPeer == null)' `
    'Battle leave does not reject a missing remote NetPeer.'
Require-Text $joinLeave `
    '!objectManager.TryGetObjectWithLogging<PartyBase>(payload.What.PartyId, out var requestedParty)' `
    'Battle leave does not resolve the requested PartyId before removal.'
Require-Text $joinLeave `
    '!TryGetRequestingPlayer(requestingPeer, requestedParty, out var controllerId)' `
    'Battle leave does not bind the requested PartyId to the authenticated player.'
Require-Text $joinLeave `
    'RemovePartyFromBattleAndBroadcast(' `
    'Battle leave no longer reaches the authoritative removal path after ownership validation.'

$unsafeLeave = @'
        RemovePartyFromBattleAndBroadcast(
            payload.What.PartyId,
            payload.What.FinishLocalMenus,
            payload.Who as NetPeer);
'@ -replace "`r`n", "`n"
if ($joinLeave.Contains($unsafeLeave)) {
    throw 'Legacy battle-leave path is still present: client PartyId reaches removal without ownership validation.'
}

# Exit isolation: a remote teardown must first resolve the exact live peer to a persistent player,
# then to that player's authoritative MobileParty, and finally prove membership in this MapEvent.
# Bannerlord 1.3.15 exposes InvolvedParties but not the later single-argument FindMapEventParty API.
Require-Text $finalize `
    'if (!playerManager.TryGetPlayer(requester, out var requestingPlayer))' `
    'Mission finalize does not reject unknown/stale NetPeer requesters.'
Require-Text $finalize `
    '!objectManager.TryGetObject<MobileParty>(requestingPlayer.MobilePartyId, out var requestingParty)' `
    'Mission finalize does not resolve the requester authoritative MobileParty.'
Require-Text $finalize `
    'mapEvent.InvolvedParties == null' `
    'Mission finalize does not guard missing authoritative MapEvent membership.'
Require-Text $finalize `
    '!mapEvent.InvolvedParties.Contains(requestingParty.Party)' `
    'Mission finalize does not reject players outside the target MapEvent using the 1.3.15 API.'
Require-Text $finalize `
    'if (hostRegistry.TryGet(payload.What.MapEventId, out var hostAssignment)' `
    'Mission finalize no longer enforces the elected battle host when one exists.'
Require-Text $finalize `
    'requestingPlayer.ControllerId != hostAssignment.HostControllerId' `
    'Mission finalize host ownership comparison is missing.'

$legacyBypass = @'
if (requester != null && hostRegistry.TryGet(payload.What.MapEventId, out var hostAssignment)
            && playerManager.TryGetPlayer(requester, out var requestingPlayer)
'@ -replace "`r`n", "`n"
if ($finalize.Contains($legacyBypass)) {
    throw 'Legacy finalize authorization is still present: an unknown NetPeer can bypass the host check.'
}

Write-Host 'KaiTOR four-player mission enter/leave/exit isolation staging: PASS'

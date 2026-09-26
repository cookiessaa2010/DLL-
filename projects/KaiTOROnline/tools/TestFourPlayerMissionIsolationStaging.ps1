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

# Exit isolation: preserve upstream finalize handling, but require an additional participant-ownership
# gate after the MapEvent is resolved. The host-election guard may remain earlier in the method;
# participant membership is the fail-closed backstop for stale/unknown peers and hostless events.
Require-Text $finalize `
    'if (!playerManager.TryGetPlayer(requester, out var finalizingPlayer))' `
    'Mission finalize does not reject unknown/stale NetPeer requesters.'
Require-Text $finalize `
    '!objectManager.TryGetObject<MobileParty>(' `
    'Mission finalize does not resolve the requester authoritative MobileParty.'
Require-Text $finalize `
    'finalizingPlayer.MobilePartyId' `
    'Mission finalize does not bind membership to the requester registered MobileParty.'
Require-Text $finalize `
    'mapEvent.InvolvedParties == null' `
    'Mission finalize does not guard missing authoritative MapEvent membership.'
Require-Text $finalize `
    '!mapEvent.InvolvedParties.Contains(finalizingParty.Party)' `
    'Mission finalize does not reject players outside the target MapEvent using the 1.3.15 API.'
Require-Text $finalize `
    'if (requester != null && hostRegistry.TryGet(payload.What.MapEventId, out var hostAssignment)' `
    'Mission finalize no longer preserves upstream elected-host enforcement.'
Require-Text $finalize `
    'requestingPlayer.ControllerId != hostAssignment.HostControllerId' `
    'Mission finalize host ownership comparison is missing.'

$mapResolveIndex = $finalize.IndexOf('if (!objectManager.TryGetObjectWithLogging(payload.What.MapEventId, out MapEvent mapEvent))', [StringComparison]::Ordinal)
$participantGateIndex = $finalize.IndexOf('if (!playerManager.TryGetPlayer(requester, out var finalizingPlayer))', [StringComparison]::Ordinal)
$membershipIndex = $finalize.IndexOf('!mapEvent.InvolvedParties.Contains(finalizingParty.Party)', [StringComparison]::Ordinal)
if ($mapResolveIndex -lt 0 -or $participantGateIndex -lt 0 -or $membershipIndex -lt 0 -or
    $mapResolveIndex -gt $participantGateIndex -or $participantGateIndex -gt $membershipIndex) {
    throw 'Mission finalize participant ownership must execute after MapEvent resolution and before finalization.'
}

Write-Host 'KaiTOR four-player mission enter/leave/exit isolation staging: PASS'

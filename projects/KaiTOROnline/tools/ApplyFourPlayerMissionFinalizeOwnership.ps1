param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'

$path = Join-Path $UpstreamRoot 'source/GameInterface/Services/MapEvents/Handlers/BattleFinalizeHandler.cs'
if (-not (Test-Path -LiteralPath $path)) {
    throw 'Missing upstream BattleFinalizeHandler.cs'
}

$text = [IO.File]::ReadAllText($path) -replace "`r`n", "`n"

$old = @'
        var requester = payload.Who as NetPeer;

        // Only the elected battle host may finalize a live shared battle: a client whose local mission
        // concluded early still runs vanilla's FinalizeEvent back on the map, and applying that here
        // would tear the battle down under everyone else (forced encounter close mid-mission). No reply
        // on a refusal — the finalized reply would pull the refused client off the menu vanilla parked
        // it on. Server-local publishes (requester null) and battles with no elected host pass.
        if (requester != null && hostRegistry.TryGet(payload.What.MapEventId, out var hostAssignment)
            && playerManager.TryGetPlayer(requester, out var requestingPlayer)
            && requestingPlayer.ControllerId != hostAssignment.HostControllerId)
        {
            Logger.Information("Refused finalize of {MapEventId} from non-host {ControllerId}",
                payload.What.MapEventId, requestingPlayer.ControllerId);
            return;
        }

        if (!objectManager.TryGetObjectWithLogging(payload.What.MapEventId, out MapEvent mapEvent))
        {
            if (requester != null)
                network.Send(requester, new NetworkMapEventFinalized());
            else
                messageBroker.Publish(this, new NetworkMapEventFinalized());

            return;
        }
'@ -replace "`r`n", "`n"

$new = @'
        var requester = payload.Who as NetPeer;

        if (!objectManager.TryGetObjectWithLogging(payload.What.MapEventId, out MapEvent mapEvent))
        {
            if (requester != null)
                network.Send(requester, new NetworkMapEventFinalized());
            else
                messageBroker.Publish(this, new NetworkMapEventFinalized());

            return;
        }

        // A remote finalize is authoritative only for a registered player whose persistent MobileParty
        // is still a member of this exact MapEvent. Without this gate an unknown/stale NetPeer bypasses
        // the old host-only conditional and can tear down another pair's live mission while it is being
        // fought. This is the mission-exit counterpart to KaiTOR's encounter and movement ownership gates.
        if (requester != null)
        {
            if (!playerManager.TryGetPlayer(requester, out var requestingPlayer))
            {
                Logger.Warning("Rejecting finalize of {MapEventId}: requester is not a registered player",
                    payload.What.MapEventId);
                return;
            }

            if (!objectManager.TryGetObject<MobileParty>(requestingPlayer.MobilePartyId, out var requestingParty)
                || mapEvent.FindMapEventParty(requestingParty.Party) == null)
            {
                Logger.Warning("Rejecting finalize of {MapEventId} from non-participant {ControllerId}",
                    payload.What.MapEventId, requestingPlayer.ControllerId);
                return;
            }

            // Only the elected battle host may finalize a live shared battle: a participant whose local
            // mission concluded early must not tear the event down under the other mission participants.
            if (hostRegistry.TryGet(payload.What.MapEventId, out var hostAssignment)
                && requestingPlayer.ControllerId != hostAssignment.HostControllerId)
            {
                Logger.Information("Refused finalize of {MapEventId} from non-host {ControllerId}",
                    payload.What.MapEventId, requestingPlayer.ControllerId);
                return;
            }
        }
'@ -replace "`r`n", "`n"

if (-not $text.Contains($old)) {
    throw 'Mission finalize ownership anchor not found in pinned upstream BattleFinalizeHandler.cs'
}

[IO.File]::WriteAllText(
    $path,
    $text.Replace($old, $new),
    [Text.UTF8Encoding]::new($false))

Write-Host 'KaiTOR 4P mission-finalize ownership gate applied successfully.'

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

# Keep the upstream finalize flow intact (including newer shared-hideout handling) and inject only
# KaiTOR's additional authority invariant after the target MapEvent has been resolved.
$anchor = @'
        if (!objectManager.TryGetObjectWithLogging(payload.What.MapEventId, out MapEvent mapEvent))
        {
            if (requester != null)
                network.Send(requester, new NetworkMapEventFinalized());
            else
                messageBroker.Publish(this, new NetworkMapEventFinalized());

            return;
        }
'@ -replace "`r`n", "`n"

$replacement = @'
        if (!objectManager.TryGetObjectWithLogging(payload.What.MapEventId, out MapEvent mapEvent))
        {
            if (requester != null)
                network.Send(requester, new NetworkMapEventFinalized());
            else
                messageBroker.Publish(this, new NetworkMapEventFinalized());

            return;
        }

        // A remote finalize is authoritative only for a registered player whose persistent
        // MobileParty is still a member of this exact MapEvent. This closes the gap where an
        // unknown/stale NetPeer could otherwise reach finalization when no host assignment exists.
        if (requester != null)
        {
            if (!playerManager.TryGetPlayer(requester, out var finalizingPlayer))
            {
                Logger.Warning(
                    "Rejecting finalize of {MapEventId}: requester is not a registered player",
                    payload.What.MapEventId);
                return;
            }

            if (!objectManager.TryGetObject<MobileParty>(
                    finalizingPlayer.MobilePartyId,
                    out var finalizingParty) ||
                mapEvent.InvolvedParties == null ||
                !mapEvent.InvolvedParties.Contains(finalizingParty.Party))
            {
                Logger.Warning(
                    "Rejecting finalize of {MapEventId} from non-participant {ControllerId}",
                    payload.What.MapEventId,
                    finalizingPlayer.ControllerId);
                return;
            }
        }
'@ -replace "`r`n", "`n"

if ($text.Contains('Rejecting finalize of {MapEventId}: requester is not a registered player')) {
    Write-Host 'KaiTOR mission-finalize participant ownership is already present.'
}
elseif (-not $text.Contains($anchor)) {
    throw 'Mission finalize MapEvent-resolution anchor not found; refusing to alter unknown upstream flow.'
}
else {
    $text = $text.Replace($anchor, $replacement)
    [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
}

Write-Host 'KaiTOR 4P mission-finalize ownership gate applied successfully.'

param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'

$path = Join-Path $UpstreamRoot 'source/GameInterface/Services/MapEvents/Handlers/BattleJoinLeaveHandler.cs'
if (-not (Test-Path -LiteralPath $path)) {
    throw 'Missing upstream BattleJoinLeaveHandler.cs'
}

$text = [IO.File]::ReadAllText($path) -replace "`r`n", "`n"

$old = @'
    /// <summary>[Server] A client asked to leave a battle without ending it.</summary>
    private void Handle_NetworkRequestLeaveBattle(MessagePayload<NetworkRequestLeaveBattle> payload)
    {
        if (ModInformation.IsClient) return;

        RemovePartyFromBattleAndBroadcast(
            payload.What.PartyId,
            payload.What.FinishLocalMenus,
            payload.Who as NetPeer);
    }
'@ -replace "`r`n", "`n"

$new = @'
    /// <summary>[Server] A client asked to leave a battle without ending it.</summary>
    private void Handle_NetworkRequestLeaveBattle(MessagePayload<NetworkRequestLeaveBattle> payload)
    {
        if (ModInformation.IsClient) return;

        var requestingPeer = payload.Who as NetPeer;
        if (requestingPeer == null)
        {
            Logger.Warning("Ignoring remote battle-leave request without a NetPeer for party {PartyId}",
                payload.What.PartyId);
            return;
        }

        if (!objectManager.TryGetObjectWithLogging<PartyBase>(payload.What.PartyId, out var requestedParty) ||
            !TryGetRequestingPlayer(requestingPeer, requestedParty, out var controllerId))
        {
            Logger.Warning("Ignoring battle-leave request: peer does not control party {PartyId}",
                payload.What.PartyId);
            return;
        }

        Logger.Debug("Accepted battle-leave request from {ControllerId} for owned party {PartyId}",
            controllerId, payload.What.PartyId);

        RemovePartyFromBattleAndBroadcast(
            payload.What.PartyId,
            payload.What.FinishLocalMenus,
            requestingPeer);
    }
'@ -replace "`r`n", "`n"

if (-not $text.Contains($old)) {
    throw 'Battle leave ownership anchor not found in pinned upstream BattleJoinLeaveHandler.cs'
}

[IO.File]::WriteAllText(
    $path,
    $text.Replace($old, $new),
    [Text.UTF8Encoding]::new($false))

Write-Host 'KaiTOR 4P battle-leave ownership gate applied successfully.'

param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'

$relativePath = 'source/GameInterface/Services/MapEvents/Handlers/ConversationRequestHandler.cs'
$path = Join-Path $UpstreamRoot $relativePath
if (-not (Test-Path -LiteralPath $path)) {
    throw "Missing upstream encounter handler: $relativePath"
}

$text = [IO.File]::ReadAllText($path) -replace "`r`n", "`n"

$old = @'
        if (!objectManager.TryGetObjectWithLogging<PartyBase>(request.AttackerId, out var attacker)) return;
        if (!objectManager.TryGetObjectWithLogging<PartyBase>(request.DefenderId, out var defender)) return;

        if (!TryAcceptConversationRequest(requestingPeer, request, attacker, defender, out var aiParty, out var aiPartyId, out var playerPartyId, out var isPlayerVsPlayer))
            return;
'@

$new = @'
        if (!objectManager.TryGetObjectWithLogging<PartyBase>(request.AttackerId, out var attacker)) return;
        if (!objectManager.TryGetObjectWithLogging<PartyBase>(request.DefenderId, out var defender)) return;

        // KaiTOR 4P: attacker/defender ids come from the client. Before any encounter/conversation
        // approval, bind the request to the authenticated NetPeer's registered MobileParty so one
        // controller cannot start or join an encounter on behalf of another player's party.
        if (!RequesterOwnsRequestedPlayerParty(requestingPeer, attacker, defender))
        {
            Logger.Warning(
                "Rejecting conversation request: peer does not own either requested player party. AttackerId={AttackerId}, DefenderId={DefenderId}",
                request.AttackerId,
                request.DefenderId);
            network.Send(requestingPeer, new NetworkConversationDenied(ConversationDeniedReason.PlayerUnavailable, request.RequestId));
            return;
        }

        if (!TryAcceptConversationRequest(requestingPeer, request, attacker, defender, out var aiParty, out var aiPartyId, out var playerPartyId, out var isPlayerVsPlayer))
            return;
'@

if (-not $text.Contains($old)) {
    throw "Encounter ownership anchor not found in $relativePath"
}

$text = $text.Replace($old, $new)

$helperAnchor = @'
    /// <summary>
    /// [Server] Identifies the AI side and rejects when both parties are already in
'@

$helper = @'
    private bool RequesterOwnsRequestedPlayerParty(NetPeer requestingPeer, PartyBase attacker, PartyBase defender)
    {
        if (requestingPeer == null ||
            !playerManager.TryGetPlayer(requestingPeer, out var player) ||
            !objectManager.TryGetObject<MobileParty>(player.MobilePartyId, out var requestingParty) ||
            requestingParty?.Party == null)
        {
            return false;
        }

        return ReferenceEquals(requestingParty.Party, attacker) ||
               ReferenceEquals(requestingParty.Party, defender);
    }

    /// <summary>
    /// [Server] Identifies the AI side and rejects when both parties are already in
'@

if (-not $text.Contains($helperAnchor)) {
    throw "Encounter ownership helper anchor not found in $relativePath"
}

$text = $text.Replace($helperAnchor, $helper)
[IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))

Write-Host 'KaiTOR four-player encounter requester ownership applied successfully.'

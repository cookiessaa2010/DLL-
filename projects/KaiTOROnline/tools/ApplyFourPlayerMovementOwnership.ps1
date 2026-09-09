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

$handler = 'source/Coop.Core/Server/Services/MobileParties/PacketHandlers/PartyAIBehaviorPacketHandler.cs'

Replace-Exact `
    $handler `
    'using GameInterface.Services.MobileParties.Messages.Behavior;' `
    "using GameInterface.Services.MobileParties.Messages.Behavior;`nusing GameInterface.Services.Players;"

Replace-Exact `
    $handler `
    '    private readonly IMessageBroker messageBroker;
    private readonly INetwork network;' `
    '    private readonly IMessageBroker messageBroker;
    private readonly INetwork network;
    private readonly IPlayerManager playerManager;'

Replace-Exact `
    $handler `
    '        IMessageBroker messageBroker,
        INetwork network)' `
    '        IMessageBroker messageBroker,
        INetwork network,
        IPlayerManager playerManager)'

Replace-Exact `
    $handler `
    '        this.messageBroker = messageBroker;
        this.network = network;
        packetManager.RegisterPacketHandler(this);' `
    '        this.messageBroker = messageBroker;
        this.network = network;
        this.playerManager = playerManager;
        packetManager.RegisterPacketHandler(this);'

Replace-Exact `
    $handler `
    '        var data = convertedPacket.BehaviorUpdateData;

        messageBroker.Publish(this, new UpdatePartyBehavior(ref data));' `
    '        var data = convertedPacket.BehaviorUpdateData;

        // Campaign movement is server-authoritative per connected player. Never trust a
        // client-supplied party id or OriginControllerId: resolve identity from the NetPeer and
        // only allow that peer to issue behavior changes for its registered MobileParty.
        if (!playerManager.TryGetPlayer(peer, out var player))
            return;

        var requestedPartyId = Compact(data.MobilePartyId, typeof(MobileParty));
        var ownedPartyId = Compact(player.MobilePartyId, typeof(MobileParty));
        if (!string.Equals(requestedPartyId, ownedPartyId, System.StringComparison.Ordinal))
            return;

        data.OriginControllerId = player.ControllerId;
        messageBroker.Publish(this, new UpdatePartyBehavior(ref data));'

Write-Host 'KaiTOR 4-player movement ownership gate applied successfully.'
Write-Host 'Each connected NetPeer may update only its registered MobileParty.'

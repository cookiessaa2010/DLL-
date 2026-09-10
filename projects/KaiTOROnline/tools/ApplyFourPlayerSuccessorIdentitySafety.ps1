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
        throw "Successor identity anchor not found in $Path`n--- anchor ---`n$oldNormalized"
    }

    [IO.File]::WriteAllText(
        $fullPath,
        $text.Replace($oldNormalized, $newNormalized),
        [Text.UTF8Encoding]::new($false))
}

# Character creation happens after the four-player admission gate has already bound this exact
# NetPeer object to one persistent controller id. NetworkTransferNewHero also carries a PlayerId,
# but that value is client supplied and must never be allowed to replace the admitted identity.
Replace-Exact `
    'source/Coop.Core/Server/Connections/PlayerAdmissionGate.cs' `
    @'
        AdmissionDecision TryAdmit(string controllerId, NetPeer peer);
        bool Release(NetPeer peer);
'@ `
    @'
        AdmissionDecision TryAdmit(string controllerId, NetPeer peer);
        bool TryGetControllerId(NetPeer peer, out string controllerId);
        bool Release(NetPeer peer);
'@

Replace-Exact `
    'source/Coop.Core/Server/Connections/PlayerAdmissionGate.cs' `
    @'
        public bool Release(NetPeer peer)
        {
'@ `
    @'
        public bool TryGetControllerId(NetPeer peer, out string controllerId)
        {
            controllerId = null;
            if (peer == null)
                return false;

            lock (sync)
            {
                return peerToController.TryGetValue(peer, out controllerId);
            }
        }

        public bool Release(NetPeer peer)
        {
'@

Replace-Exact `
    'source/Coop.Core/Server/Connections/ConnectionLogic.cs' `
    '[typeof(CreateCharacterState)] = () => new CreateCharacterState(this, context.ObjectManager, context.MessageBroker, context.Network, context.HeroInterface, context.PlayerManager, context.PlayerCreationRollback, context.ExistingPlayerSender),' `
    '[typeof(CreateCharacterState)] = () => new CreateCharacterState(this, context.ObjectManager, context.MessageBroker, context.Network, context.HeroInterface, context.PlayerManager, context.PlayerCreationRollback, context.ExistingPlayerSender, context.PlayerAdmissionGate),'

Replace-Exact `
    'source/Coop.Core/Server/Connections/States/CreateCharacterState.cs' `
    @'
    private readonly IPlayerCreationRollback playerCreationRollback;
    private readonly IExistingPlayerSender existingPlayerSender;
'@ `
    @'
    private readonly IPlayerCreationRollback playerCreationRollback;
    private readonly IExistingPlayerSender existingPlayerSender;
    private readonly IPlayerAdmissionGate playerAdmissionGate;
'@

Replace-Exact `
    'source/Coop.Core/Server/Connections/States/CreateCharacterState.cs' `
    @'
        IPlayerManager playerManager,
        IPlayerCreationRollback playerCreationRollback,
        IExistingPlayerSender existingPlayerSender)
'@ `
    @'
        IPlayerManager playerManager,
        IPlayerCreationRollback playerCreationRollback,
        IExistingPlayerSender existingPlayerSender,
        IPlayerAdmissionGate playerAdmissionGate)
'@

Replace-Exact `
    'source/Coop.Core/Server/Connections/States/CreateCharacterState.cs' `
    @'
        this.playerCreationRollback = playerCreationRollback;
        this.existingPlayerSender = existingPlayerSender;
        messageBroker.Subscribe<NetworkTransferNewHero>(Handle_NetworkTransferNewHero);
'@ `
    @'
        this.playerCreationRollback = playerCreationRollback;
        this.existingPlayerSender = existingPlayerSender;
        this.playerAdmissionGate = playerAdmissionGate;
        messageBroker.Subscribe<NetworkTransferNewHero>(Handle_NetworkTransferNewHero);
'@

Replace-Exact `
    'source/Coop.Core/Server/Connections/States/CreateCharacterState.cs' `
    @'
        if (netPeer != ConnectionLogic.Peer) return;

        var controllerId = obj.What.PlayerId;
        var data = obj.What.PlayerHero;
'@ `
    @'
        if (netPeer != ConnectionLogic.Peer) return;

        // The admitted slot is the authoritative identity for this connection. Never trust the
        // PlayerId repeated in the hero-upload packet: a stale or malicious client must not be able
        // to turn a successor creation into another controller's Hero/Clan/MobileParty graph.
        if (!playerAdmissionGate.TryGetControllerId(netPeer, out var admittedControllerId))
        {
            Logger.Warning(
                "Rejecting character creation from peer {Peer}: no active KaiTOR admission identity",
                netPeer?.Id);
            ConnectionLogic.Peer.Disconnect();
            return;
        }

        if (!string.Equals(admittedControllerId, obj.What.PlayerId, System.StringComparison.Ordinal))
        {
            Logger.Warning(
                "Rejecting character creation identity mismatch for peer {Peer}: admitted {AdmittedControllerId}, packet requested {RequestedControllerId}",
                netPeer.Id,
                admittedControllerId,
                obj.What.PlayerId);
            ConnectionLogic.Peer.Disconnect();
            return;
        }

        var controllerId = admittedControllerId;
        var data = obj.What.PlayerHero;
'@

Write-Host 'KaiTOR four-player successor/character-creation identity binding applied successfully.'
Write-Host 'NetworkTransferNewHero PlayerId is now checked against the already admitted NetPeer controller identity.'

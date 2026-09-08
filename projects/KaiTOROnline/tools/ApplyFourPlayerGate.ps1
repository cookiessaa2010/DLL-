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
    if (-not (Test-Path $fullPath)) {
        throw "Missing upstream file: $Path"
    }

    $text = [IO.File]::ReadAllText($fullPath)
    if (-not $text.Contains($Old)) {
        throw "Anchor not found in $Path`n--- anchor ---`n$Old"
    }

    $updated = $text.Replace($Old, $New)
    [IO.File]::WriteAllText($fullPath, $updated, [Text.UTF8Encoding]::new($false))
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$overlay = Join-Path $projectRoot 'upstream-overlay/source/Coop.Core/Server/Connections/PlayerAdmissionGate.cs'
$target = Join-Path $UpstreamRoot 'source/Coop.Core/Server/Connections/PlayerAdmissionGate.cs'

if (-not (Test-Path $overlay)) {
    throw "Missing KaiTOR overlay file: $overlay"
}

Copy-Item $overlay $target -Force

Replace-Exact `
    'source/Coop.Core/Server/Connections/ConnectionModule.cs' `
    '        builder.RegisterType<ExistingPlayerSender>().As<IExistingPlayerSender>().InstancePerLifetimeScope();' `
    "        builder.RegisterType<ExistingPlayerSender>().As<IExistingPlayerSender>().InstancePerLifetimeScope();`r`n        builder.RegisterType<PlayerAdmissionGate>().As<IPlayerAdmissionGate>().InstancePerLifetimeScope();"

Replace-Exact `
    'source/Coop.Core/Server/Connections/ConnectionContext.cs' `
    "        IExistingPlayerSender existingPlayerSender,`r`n        ISteamBanList steamBanList,`r`n        IServerOptionsProvider serverOptionsProvider," `
    "        IExistingPlayerSender existingPlayerSender,`r`n        ISteamBanList steamBanList,`r`n        IPlayerAdmissionGate playerAdmissionGate,`r`n        IServerOptionsProvider serverOptionsProvider,"

Replace-Exact `
    'source/Coop.Core/Server/Connections/ConnectionContext.cs' `
    "        ExistingPlayerSender = existingPlayerSender;`r`n        SteamBanList = steamBanList;`r`n        ServerOptionsProvider = serverOptionsProvider;" `
    "        ExistingPlayerSender = existingPlayerSender;`r`n        SteamBanList = steamBanList;`r`n        PlayerAdmissionGate = playerAdmissionGate;`r`n        ServerOptionsProvider = serverOptionsProvider;"

Replace-Exact `
    'source/Coop.Core/Server/Connections/ConnectionContext.cs' `
    "    public IExistingPlayerSender ExistingPlayerSender { get; }`r`n    public ISteamBanList SteamBanList { get; }`r`n    public IServerOptionsProvider ServerOptionsProvider { get; }" `
    "    public IExistingPlayerSender ExistingPlayerSender { get; }`r`n    public ISteamBanList SteamBanList { get; }`r`n    public IPlayerAdmissionGate PlayerAdmissionGate { get; }`r`n    public IServerOptionsProvider ServerOptionsProvider { get; }"

Replace-Exact `
    'source/Coop.Core/Server/Connections/ConnectionLogic.cs' `
    '            [typeof(ResolveCharacterState)] = () => new ResolveCharacterState(this, context.MessageBroker, context.Network, context.ModuleValidator, context.PlayerManager, context.PlayerPartyRestorer, context.ObjectManager, context.ModuleInfoProvider, context.ExistingPlayerSender, context.SteamBanList),' `
    '            [typeof(ResolveCharacterState)] = () => new ResolveCharacterState(this, context.MessageBroker, context.Network, context.ModuleValidator, context.PlayerManager, context.PlayerPartyRestorer, context.ObjectManager, context.ModuleInfoProvider, context.ExistingPlayerSender, context.SteamBanList, context.PlayerAdmissionGate),'

Replace-Exact `
    'source/Coop.Core/Server/Connections/States/ResolveCharacterState.cs' `
    "    private readonly IExistingPlayerSender existingPlayerSender;`r`n    private readonly ISteamBanList steamBanList;" `
    "    private readonly IExistingPlayerSender existingPlayerSender;`r`n    private readonly ISteamBanList steamBanList;`r`n    private readonly IPlayerAdmissionGate playerAdmissionGate;"

Replace-Exact `
    'source/Coop.Core/Server/Connections/States/ResolveCharacterState.cs' `
    "        IModuleInfoProvider moduleInfoProvider,`r`n        IExistingPlayerSender existingPlayerSender,`r`n        ISteamBanList steamBanList)" `
    "        IModuleInfoProvider moduleInfoProvider,`r`n        IExistingPlayerSender existingPlayerSender,`r`n        ISteamBanList steamBanList,`r`n        IPlayerAdmissionGate playerAdmissionGate)"

Replace-Exact `
    'source/Coop.Core/Server/Connections/States/ResolveCharacterState.cs' `
    "        this.existingPlayerSender = existingPlayerSender;`r`n        this.steamBanList = steamBanList;" `
    "        this.existingPlayerSender = existingPlayerSender;`r`n        this.steamBanList = steamBanList;`r`n        this.playerAdmissionGate = playerAdmissionGate;"

$admissionAnchor = @'
            if (steamBanList.IsBanned(obj.What.PlayerId))
            {
                Logger.Warning(
                    "Controller {ControllerId} is banned; disconnecting the joining peer",
                    obj.What.PlayerId);
                peer.Disconnect();
                return;
            }

            ResolveCharacter(peer, obj.What.PlayerId);
'@ -replace "`n", "`r`n"

$admissionReplacement = @'
            if (steamBanList.IsBanned(obj.What.PlayerId))
            {
                Logger.Warning(
                    "Controller {ControllerId} is banned; disconnecting the joining peer",
                    obj.What.PlayerId);
                peer.Disconnect();
                return;
            }

            var admission = playerAdmissionGate.TryAdmit(obj.What.PlayerId, peer);
            if (!admission.Accepted)
            {
                var reason = admission.RejectReason == AdmissionRejectReason.ServerFull
                    ? "KaiTOR Online server is full (4/4 players)."
                    : "KaiTOR Online rejected the connection because the player identity is invalid.";

                Logger.Information(
                    "Rejecting controller {ControllerId}: {Reason}",
                    obj.What.PlayerId,
                    reason);

                network.SendImmediate(peer, NetworkClientValidated.Rejected(reason));
                return;
            }

            if (admission.IsReconnect && admission.SupersededPeer != null)
            {
                Logger.Information(
                    "Controller {ControllerId} reconnected; replacing peer {OldPeer} with {NewPeer}",
                    obj.What.PlayerId,
                    admission.SupersededPeer.Id,
                    peer.Id);

                try
                {
                    admission.SupersededPeer.Disconnect();
                }
                catch (Exception disconnectFailure)
                {
                    Logger.Warning(
                        disconnectFailure,
                        "Failed to disconnect superseded peer {Peer}",
                        admission.SupersededPeer.Id);
                }
            }

            ResolveCharacter(peer, obj.What.PlayerId);
'@ -replace "`n", "`r`n"

Replace-Exact 'source/Coop.Core/Server/Connections/States/ResolveCharacterState.cs' $admissionAnchor $admissionReplacement

Replace-Exact `
    'source/Coop.Core/Server/Connections/ConnectionCollection.cs' `
    "        var playerId = obj.What.PlayerId;`r`n`r`n        if (ConnectionStates.TryRemove(playerId, out IConnectionLogic logic))" `
    "        var playerId = obj.What.PlayerId;`r`n`r`n        // Release the live network slot only; persistent Hero/Party data remains registered.`r`n        connectionContext.PlayerAdmissionGate.Release(playerId);`r`n`r`n        if (ConnectionStates.TryRemove(playerId, out IConnectionLogic logic))"

$validationOld = @'
public record NetworkClientValidated : IEvent
{
    [ProtoMember(1)]
    public bool HeroExists { get; }
    [ProtoMember(2)]
    public Player Player { get; }

    public NetworkClientValidated(bool heroExists, Player player)
    {
        HeroExists = heroExists;
        Player = player;
    }
}
'@ -replace "`n", "`r`n"

$validationNew = @'
public record NetworkClientValidated : IEvent
{
    [ProtoMember(1)]
    public bool HeroExists { get; }
    [ProtoMember(2)]
    public Player Player { get; }
    [ProtoMember(3)]
    public string RejectionReason { get; }

    public NetworkClientValidated(bool heroExists, Player player, string rejectionReason = null)
    {
        HeroExists = heroExists;
        Player = player;
        RejectionReason = rejectionReason;
    }

    public static NetworkClientValidated Rejected(string reason) =>
        new(false, null, reason);
}
'@ -replace "`n", "`r`n"

Replace-Exact 'source/Coop.Core/Server/Connections/Messages/NetworkClientValidation.cs' $validationOld $validationNew

$clientAnchor = @'
    internal void Handle_NetworkClientValidated(MessagePayload<NetworkClientValidated> obj)
    {
        if (obj.What.HeroExists)
'@ -replace "`n", "`r`n"

$clientReplacement = @'
    internal void Handle_NetworkClientValidated(MessagePayload<NetworkClientValidated> obj)
    {
        if (!string.IsNullOrWhiteSpace(obj.What.RejectionReason))
        {
            var message = obj.What.RejectionReason;
            messageBroker.Publish(this, new SendInformationMessage(message));
            disconnectReason = message;
            Logic.Disconnect();
            return;
        }

        if (obj.What.HeroExists)
'@ -replace "`n", "`r`n"

Replace-Exact 'source/Coop.Core/Client/States/ValidateModuleState.cs' $clientAnchor $clientReplacement

Write-Host 'KaiTOR four-player gate applied successfully.'
Write-Host 'MaxPlayers = 4, reconnect preserves slot, fifth distinct controller is rejected before character creation.'

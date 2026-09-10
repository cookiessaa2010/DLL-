param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'

$path = Join-Path $UpstreamRoot 'source/Coop.Core/Server/Services/Time/Handlers/TimeHandler.cs'
if (-not (Test-Path -LiteralPath $path)) {
    throw 'Missing upstream server TimeHandler.cs'
}

$text = [IO.File]::ReadAllText($path) -replace "`r`n", "`n"

$usingOld = @'
using GameInterface.Services.Heroes.Messages;
using LiteNetLib;
'@ -replace "`r`n", "`n"

$usingNew = @'
using GameInterface.Services.Heroes.Messages;
using GameInterface.Services.Players;
using LiteNetLib;
'@ -replace "`r`n", "`n"

if (-not $text.Contains($usingOld)) {
    throw 'Time authority using anchor not found in pinned upstream TimeHandler.cs'
}
$text = $text.Replace($usingOld, $usingNew)

$fieldsOld = @'
    private readonly IMessageBroker messageBroker;
    private readonly ITimeControlInterface timeControlInterface;

    public TimeHandler(IMessageBroker messageBroker, ITimeControlInterface timeControlInterface)
    {
        this.messageBroker = messageBroker;
        this.timeControlInterface = timeControlInterface;
'@ -replace "`r`n", "`n"

$fieldsNew = @'
    private readonly IMessageBroker messageBroker;
    private readonly ITimeControlInterface timeControlInterface;
    private readonly IPlayerManager playerManager;

    public TimeHandler(
        IMessageBroker messageBroker,
        ITimeControlInterface timeControlInterface,
        IPlayerManager playerManager)
    {
        this.messageBroker = messageBroker;
        this.timeControlInterface = timeControlInterface;
        this.playerManager = playerManager;
'@ -replace "`r`n", "`n"

if (-not $text.Contains($fieldsOld)) {
    throw 'Time authority constructor anchor not found in pinned upstream TimeHandler.cs'
}
$text = $text.Replace($fieldsOld, $fieldsNew)

$handlerOld = @'
    internal void Handle_NetworkRequestTimeSpeedChange(MessagePayload<NetworkRequestTimeSpeedChange> obj)
    {
        var peer = obj.Who as NetPeer;

        Logger.Information(
            "Peer requested time control: peer={PeerId} mode={RequestedMode}",
            peer?.Id,
            obj.What.NewControlMode);

        timeControlInterface.ServerSetTimeControl(obj.What.NewControlMode);
    }
'@ -replace "`r`n", "`n"

$handlerNew = @'
    internal void Handle_NetworkRequestTimeSpeedChange(MessagePayload<NetworkRequestTimeSpeedChange> obj)
    {
        var peer = obj.Who as NetPeer;
        if (peer == null || !playerManager.TryGetPlayer(peer, out var player) || !playerManager.IsConnected(player))
        {
            Logger.Warning(
                "Ignoring campaign time request from unregistered/stale peer: peer={PeerId} mode={RequestedMode}",
                peer?.Id,
                obj.What.NewControlMode);
            return;
        }

        Logger.Information(
            "Registered controller requested campaign time: controller={ControllerId} peer={PeerId} mode={RequestedMode}",
            player.ControllerId,
            peer.Id,
            obj.What.NewControlMode);

        timeControlInterface.ServerSetTimeControl(obj.What.NewControlMode);
    }
'@ -replace "`r`n", "`n"

if (-not $text.Contains($handlerOld)) {
    throw 'Time authority handler anchor not found in pinned upstream TimeHandler.cs'
}
$text = $text.Replace($handlerOld, $handlerNew)

[IO.File]::WriteAllText(
    $path,
    $text,
    [Text.UTF8Encoding]::new($false))

Write-Host 'KaiTOR 4P campaign-time authority gate applied successfully.'

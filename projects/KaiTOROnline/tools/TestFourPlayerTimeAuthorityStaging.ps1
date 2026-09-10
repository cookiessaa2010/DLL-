param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'

$timeHandlerPath = Join-Path $UpstreamRoot 'source/Coop.Core/Server/Services/Time/Handlers/TimeHandler.cs'
$occupancyPath = Join-Path $UpstreamRoot 'source/GameInterface/Services/MapEvents/Handlers/PlayerOccupancyPauseHandler.cs'

if (-not (Test-Path -LiteralPath $timeHandlerPath)) { throw 'Missing staged server TimeHandler.cs' }
if (-not (Test-Path -LiteralPath $occupancyPath)) { throw 'Missing staged PlayerOccupancyPauseHandler.cs' }

$timeHandler = [IO.File]::ReadAllText($timeHandlerPath)
$occupancy = [IO.File]::ReadAllText($occupancyPath)

if ($timeHandler -notmatch 'IPlayerManager playerManager') {
    throw 'Campaign time handler does not receive IPlayerManager for authenticated ownership.'
}
if ($timeHandler -notmatch 'playerManager\.TryGetPlayer\(peer, out var player\)') {
    throw 'Campaign time handler does not resolve the requesting NetPeer to a registered player.'
}
if ($timeHandler -notmatch 'playerManager\.IsConnected\(player\)') {
    throw 'Campaign time handler does not reject stale/disconnected player registrations.'
}
if ($timeHandler -notmatch 'Ignoring campaign time request from unregistered/stale peer') {
    throw 'Campaign time handler has no explicit rejection path for unknown/stale peers.'
}
if ($timeHandler -notmatch 'timeControlInterface\.ServerSetTimeControl\(obj\.What\.NewControlMode\)') {
    throw 'Registered campaign time requests no longer reach authoritative server time control.'
}

$ownershipIndex = $timeHandler.IndexOf('playerManager.TryGetPlayer(peer, out var player)', [StringComparison]::Ordinal)
$applyIndex = $timeHandler.IndexOf('timeControlInterface.ServerSetTimeControl(obj.What.NewControlMode)', [StringComparison]::Ordinal)
if ($ownershipIndex -lt 0 -or $applyIndex -lt 0 -or $ownershipIndex -gt $applyIndex) {
    throw 'Campaign time ownership validation does not occur before authoritative time mutation.'
}

# Preserve the upstream partial-battle policy: one or more free campaign-map players keep time
# available; automatic pause is acquired only when every connected player is occupied.
if ($occupancy -notmatch 'playerManager\.Players\.Where\(playerManager\.IsConnected\)\.ToList\(\)') {
    throw 'Occupancy policy is not based on the complete connected-player set.'
}
if ($occupancy -notmatch 'connectedPlayers\.Any\(\) && connectedPlayers\.All') {
    throw 'Occupancy policy no longer requires every connected player to be occupied before auto-pause.'
}
if ($occupancy -notmatch 'return mapEvent != null;') {
    throw 'Map-event occupancy is no longer part of automatic pause ownership.'
}
if ($occupancy -notmatch 'occupancyPauseLease = timeControlInterface\.ServerAcquireAutomaticPause\(\)') {
    throw 'All-player occupancy no longer acquires an authoritative automatic-pause lease.'
}

Write-Host 'KaiTOR 4P campaign-time authority and partial-battle occupancy staging: PASS'

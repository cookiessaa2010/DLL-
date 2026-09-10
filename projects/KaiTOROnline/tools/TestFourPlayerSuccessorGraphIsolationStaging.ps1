param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'

$path = Join-Path $UpstreamRoot 'source/Coop.Core/Server/Connections/States/CreateCharacterState.cs'
if (-not (Test-Path -LiteralPath $path)) {
    throw "Missing staged CreateCharacterState.cs: $path"
}

$text = [IO.File]::ReadAllText($path) -replace "`r`n", "`n"

$required = @(
    'foreach (var registeredPlayer in playerManager.Players)',
    'if (registeredPlayer.ControllerId == controllerId)',
    'registeredPlayer.HeroId == heroId',
    'registeredPlayer.MobilePartyId == mobilePartyId',
    'registeredPlayer.ClanId == clanId',
    'registeredPlayer.CharacterObjectId == characterObjectId',
    'Refusing character graph for {ControllerId}'
)

foreach ($needle in $required) {
    if (-not $text.Contains($needle)) {
        throw "Missing successor graph-isolation contract: $needle"
    }
}

$graphCheck = $text.IndexOf('foreach (var registeredPlayer in playerManager.Players)', [System.StringComparison]::Ordinal)
$playerCreate = $text.IndexOf('player = new Player(controllerId, heroId, mobilePartyId, clanId, characterObjectId);', [System.StringComparison]::Ordinal)
$addPlayer = $text.IndexOf('if (!playerManager.AddPlayer(player))', [System.StringComparison]::Ordinal)
$setPeer = $text.IndexOf('playerManager.SetPeer(controllerId, netPeer);', [System.StringComparison]::Ordinal)

if ($graphCheck -lt 0 -or $playerCreate -lt 0 -or $addPlayer -lt 0 -or $setPeer -lt 0) {
    throw 'Unable to locate successor graph-isolation ordering anchors.'
}

if ($graphCheck -gt $playerCreate) {
    throw 'Successor graph collision check must run before constructing the Player registration.'
}

if ($playerCreate -gt $addPlayer -or $addPlayer -gt $setPeer) {
    throw 'Character creation ordering changed: expected graph validation -> Player -> AddPlayer -> SetPeer.'
}

Write-Host 'KaiTOR four-player successor graph-isolation staging contract verified.'
Write-Host 'Cross-controller Hero/MobileParty/Clan/CharacterObject id reuse is rejected before player registration or peer binding.'

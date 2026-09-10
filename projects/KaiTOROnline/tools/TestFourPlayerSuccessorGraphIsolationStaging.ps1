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

# Runtime ordering spans two methods, so validate each method independently instead of
# comparing raw file offsets. Handle_NetworkTransferNewHero appears before TryCreatePlayer
# in this source file, which makes a whole-file AddPlayer-vs-Player-construction offset
# comparison report the opposite of the actual call order.
$tryCreateCall = $text.IndexOf('if (!TryCreatePlayer(controllerId, hero, out var player))', [System.StringComparison]::Ordinal)
$addPlayer = $text.IndexOf('if (!playerManager.AddPlayer(player))', $tryCreateCall + 1, [System.StringComparison]::Ordinal)
$setPeer = $text.IndexOf('playerManager.SetPeer(controllerId, netPeer);', $addPlayer + 1, [System.StringComparison]::Ordinal)

$tryCreateMethod = $text.IndexOf('private bool TryCreatePlayer(string controllerId, Hero hero, out Player player)', [System.StringComparison]::Ordinal)
$graphCheck = $text.IndexOf('foreach (var registeredPlayer in playerManager.Players)', $tryCreateMethod + 1, [System.StringComparison]::Ordinal)
$playerCreate = $text.IndexOf('player = new Player(controllerId, heroId, mobilePartyId, clanId, characterObjectId);', $graphCheck + 1, [System.StringComparison]::Ordinal)

if ($tryCreateCall -lt 0 -or $addPlayer -lt 0 -or $setPeer -lt 0 -or
    $tryCreateMethod -lt 0 -or $graphCheck -lt 0 -or $playerCreate -lt 0) {
    throw 'Unable to locate successor graph-isolation ordering anchors.'
}

if ($graphCheck -gt $playerCreate) {
    throw 'Successor graph collision check must run before constructing the Player registration.'
}

if ($tryCreateCall -gt $addPlayer -or $addPlayer -gt $setPeer) {
    throw 'Character creation handler ordering changed: expected TryCreatePlayer -> AddPlayer -> SetPeer.'
}

Write-Host 'KaiTOR four-player successor graph-isolation staging contract verified.'
Write-Host 'Cross-controller Hero/MobileParty/Clan/CharacterObject id reuse is rejected before player registration or peer binding.'

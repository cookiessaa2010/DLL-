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
        throw "Successor graph-isolation anchor not found in $Path`n--- anchor ---`n$oldNormalized"
    }

    [IO.File]::WriteAllText(
        $fullPath,
        $text.Replace($oldNormalized, $newNormalized),
        [Text.UTF8Encoding]::new($false))
}

# A freshly uploaded hero graph is client-originated data. Even after binding PlayerId to the
# admitted controller, none of its network object ids may alias a graph already owned by another
# registered player. Reject before AddPlayer/SetPeer and before any ownership marker can be changed.
Replace-Exact `
    'source/Coop.Core/Server/Connections/States/CreateCharacterState.cs' `
    @'
        if (!objectManager.TryGetIdWithLogging(hero.CharacterObject, out var characterObjectId))
            return false;

        player = new Player(controllerId, heroId, mobilePartyId, clanId, characterObjectId);
        return true;
'@ `
    @'
        if (!objectManager.TryGetIdWithLogging(hero.CharacterObject, out var characterObjectId))
            return false;

        foreach (var registeredPlayer in playerManager.Players)
        {
            if (registeredPlayer.ControllerId == controllerId)
                continue;

            if (registeredPlayer.HeroId == heroId ||
                registeredPlayer.MobilePartyId == mobilePartyId ||
                registeredPlayer.ClanId == clanId ||
                registeredPlayer.CharacterObjectId == characterObjectId)
            {
                Logger.Error(
                    "Refusing character graph for {ControllerId}: one or more Hero/Party/Clan/CharacterObject ids are already owned by controller {ExistingControllerId}",
                    controllerId,
                    registeredPlayer.ControllerId);
                return false;
            }
        }

        player = new Player(controllerId, heroId, mobilePartyId, clanId, characterObjectId);
        return true;
'@

Write-Host 'KaiTOR four-player successor graph isolation applied successfully.'
Write-Host 'A successor/first-character graph can no longer reuse Hero, MobileParty, Clan, or CharacterObject ids owned by another registered controller.'

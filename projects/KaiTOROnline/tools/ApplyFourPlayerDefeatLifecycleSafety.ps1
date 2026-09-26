param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'

$restorerPath = Join-Path $UpstreamRoot 'source/GameInterface/Services/Players/PlayerPartyRestorer.cs'
if (-not (Test-Path -LiteralPath $restorerPath)) {
    throw "Missing upstream player party restorer: $restorerPath"
}

$restorer = [IO.File]::ReadAllText($restorerPath) -replace "`r`n", "`n"

# Current upstream owns the dead-player lifecycle: keep the dead Hero's persistent party mapping
# intact so campaign synchronization can route the player into Heir Selection. Never regress to
# KaiTOR's former "delete registration and create a new character" fallback.
$deadRestore = @'
        // A dead registered hero is waiting for heir selection. Keep its saved party association
        // intact instead of restoring the dead hero into the roster as its leader.
        if (hero.IsDead)
        {
            return objectManager.TryGetObjectWithLogging(player.MobilePartyId, out MobileParty _);
        }
'@ -replace "`r`n", "`n"

if (-not $restorer.Contains($deadRestore)) {
    throw 'Upstream dead-Hero heir-selection restore semantics are missing; refusing to reintroduce the old successor fallback.'
}
Write-Host 'Upstream dead-Hero restore path preserves the registered party for Heir Selection.'

$visibilityPath = Join-Path $UpstreamRoot 'source/Coop.Core/Server/Services/Players/Handlers/PlayerPartyVisibilityHandler.cs'
if (-not (Test-Path -LiteralPath $visibilityPath)) {
    throw "Missing upstream player party visibility handler: $visibilityPath"
}

$visibility = [IO.File]::ReadAllText($visibilityPath) -replace "`r`n", "`n"

foreach ($requiredMarker in @(
    'PlayerHeirSelectionRequested',
    'PlayerHeirSelectionCompleted',
    'if (hero.IsDead)',
    'messageBroker.Publish(this, new PlayerHeirSelectionRequested(hero));'
)) {
    if (-not $visibility.Contains($requiredMarker)) {
        throw "Upstream Heir Selection lifecycle marker missing: $requiredMarker"
    }
}

$releaseOld = @'
        ActivateParty(party, player.MobilePartyId);
        Logger.Information("Restored released party {PartyId} for peer {Peer}", party.StringId, peer.Id);
'@ -replace "`r`n", "`n"

$releaseNew = @'
        // Release notifications can race hero death/captivity replication. Re-resolve the
        // authoritative Hero immediately before activation so a stale release cannot resurrect a
        // dead controller or expose a party while the Hero is still a prisoner.
        if (!objectManager.TryGetObject(player.HeroId, out Hero hero) ||
            hero.IsDead ||
            hero.IsPrisoner ||
            hero.PartyBelongedToAsPrisoner != null)
        {
            Logger.Warning(
                "Kept released party {PartyId} parked for controller {ControllerId}: hero is dead, unresolved, or still captive",
                party.StringId,
                player.ControllerId);
            return;
        }

        ActivateParty(party, player.MobilePartyId);
        Logger.Information("Restored released party {PartyId} for peer {Peer}", party.StringId, peer.Id);
'@ -replace "`r`n", "`n"

if ($visibility.Contains('hero.PartyBelongedToAsPrisoner != null')) {
    # Do not use this broad marker alone for idempotency: other upstream handlers also inspect it.
    $alreadyHardened = $visibility.Contains(
        'Kept released party {PartyId} parked for controller {ControllerId}: hero is dead, unresolved, or still captive')
}
else {
    $alreadyHardened = $false
}

if ($alreadyHardened) {
    Write-Host 'KaiTOR captivity-release Hero-state guard is already present.'
}
elseif (-not $visibility.Contains($releaseOld)) {
    throw 'Captivity release activation anchor not found in PlayerPartyVisibilityHandler.cs'
}
else {
    $visibility = $visibility.Replace($releaseOld, $releaseNew)
    [IO.File]::WriteAllText($visibilityPath, $visibility, [Text.UTF8Encoding]::new($false))
}

# ResolveCharacterState must remain on upstream semantics. A dead registered Hero is still a valid
# persistent player registration: PlayerPartyRestorer returns the existing party, the save transfers,
# and PlayerPartyVisibilityHandler opens Heir Selection after campaign synchronization.
$resolvePath = Join-Path $UpstreamRoot 'source/Coop.Core/Server/Connections/States/ResolveCharacterState.cs'
if (-not (Test-Path -LiteralPath $resolvePath)) {
    throw "Missing upstream resolve-character state: $resolvePath"
}
$resolve = [IO.File]::ReadAllText($resolvePath) -replace "`r`n", "`n"
foreach ($forbidden in @(
    'heroIsDead',
    'releasing the old registration and creating a successor',
    'ConnectionLogic.CreateCharacter();'
)) {
    if ($forbidden -eq 'ConnectionLogic.CreateCharacter();') {
        # Normal first-character creation at the end of ResolveCharacter is expected.
        continue
    }
    if ($resolve.Contains($forbidden)) {
        throw "Legacy KaiTOR dead-controller successor fallback is still present: $forbidden"
    }
}

Write-Host 'KaiTOR defeat lifecycle guards applied successfully.'
Write-Host '  dead Hero -> persistent registration/party preserved -> upstream Heir Selection'
Write-Host '  prisoner/release -> authoritative Hero state rechecked before party activation'

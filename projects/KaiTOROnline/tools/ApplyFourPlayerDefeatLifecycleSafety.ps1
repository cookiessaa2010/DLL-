param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'

$restorerPath = Join-Path $UpstreamRoot 'source/GameInterface/Services/Players/PlayerPartyRestorer.cs'
if (-not (Test-Path -LiteralPath $restorerPath)) {
    throw "Missing upstream player party restorer: $restorerPath"
}

$text = [IO.File]::ReadAllText($restorerPath) -replace "`r`n", "`n"

$old = @'
        if (!objectManager.TryGetObjectWithLogging(player.HeroId, out Hero hero)) return false;
        if (hero.Clan == null || hero.CharacterObject == null)
'@ -replace "`r`n", "`n"

$new = @'
        if (!objectManager.TryGetObjectWithLogging(player.HeroId, out Hero hero)) return false;

        // Never fabricate or reactivate a campaign party for a permanently dead player hero.
        // A successor/respawn policy must be an explicit multiplayer transition rather than an
        // accidental side effect of reconnect or save restoration.
        if (hero.IsDead)
        {
            Logger.Warning(
                "Cannot restore player {ControllerId}: hero {HeroId} is dead; successor handling is required",
                player.ControllerId,
                player.HeroId);
            return false;
        }

        if (hero.Clan == null || hero.CharacterObject == null)
'@ -replace "`r`n", "`n"

if (-not $text.Contains($old)) {
    throw "Dead-player recovery anchor not found in PlayerPartyRestorer.cs"
}

$text = $text.Replace($old, $new)
[IO.File]::WriteAllText($restorerPath, $text, [Text.UTF8Encoding]::new($false))

$visibilityPath = Join-Path $UpstreamRoot 'source/Coop.Core/Server/Services/Players/Handlers/PlayerPartyVisibilityHandler.cs'
if (-not (Test-Path -LiteralPath $visibilityPath)) {
    throw "Missing upstream player party visibility handler: $visibilityPath"
}

$visibility = [IO.File]::ReadAllText($visibilityPath) -replace "`r`n", "`n"

$releaseOld = @'
        ActivateParty(party, player.MobilePartyId);
        Logger.Information("Restored released party {PartyId} for peer {Peer}", party.StringId, peer.Id);
'@ -replace "`r`n", "`n"

$releaseNew = @'
        // Release notifications can race hero death/captivity state replication. Re-resolve the
        // authoritative Hero immediately before activation so a stale release cannot resurrect a dead
        // controller or expose a party while the Hero is still held as a prisoner.
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

if (-not $visibility.Contains($releaseOld)) {
    throw "Captivity release activation anchor not found in PlayerPartyVisibilityHandler.cs"
}

$visibility = $visibility.Replace($releaseOld, $releaseNew)
[IO.File]::WriteAllText($visibilityPath, $visibility, [Text.UTF8Encoding]::new($false))

Write-Host 'KaiTOR dead-player recovery and captivity-release lifecycle guards applied successfully.'

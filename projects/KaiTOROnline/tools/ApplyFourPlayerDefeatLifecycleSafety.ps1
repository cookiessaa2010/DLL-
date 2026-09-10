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

$resolvePath = Join-Path $UpstreamRoot 'source/Coop.Core/Server/Connections/States/ResolveCharacterState.cs'
if (-not (Test-Path -LiteralPath $resolvePath)) {
    throw "Missing upstream resolve-character state: $resolvePath"
}

$resolve = [IO.File]::ReadAllText($resolvePath) -replace "`r`n", "`n"

$resolveOld = @'
            var heroExists = false;
            var partyRestored = false;
            var registrationReplaced = false;
            var restoredPlayer = player;

            // Resolve campaign objects and repair the registration on the game thread. A loaded
            // party can be registered by an earlier queued apply even though this poll-thread
            // validation has already arrived.
            GameThread.Run(() =>
            {
                heroExists = objectManager.TryGetObjectWithLogging(player.HeroId, out Hero _);
                if (!heroExists) return;

                partyRestored = playerPartyRestorer.TryRestore(player, out restoredPlayer);
'@ -replace "`r`n", "`n"

$resolveNew = @'
            var heroExists = false;
            var heroIsDead = false;
            var partyRestored = false;
            var registrationReplaced = false;
            var restoredPlayer = player;

            // Resolve campaign objects and repair the registration on the game thread. A loaded
            // party can be registered by an earlier queued apply even though this poll-thread
            // validation has already arrived.
            GameThread.Run(() =>
            {
                heroExists = objectManager.TryGetObjectWithLogging(player.HeroId, out Hero hero);
                if (!heroExists) return;

                heroIsDead = hero.IsDead;
                if (heroIsDead) return;

                partyRestored = playerPartyRestorer.TryRestore(player, out restoredPlayer);
'@ -replace "`r`n", "`n"

if (-not $resolve.Contains($resolveOld)) {
    throw "Dead-controller successor detection anchor not found in ResolveCharacterState.cs"
}

$resolve = $resolve.Replace($resolveOld, $resolveNew)

$successorAnchor = @'
            if (heroExists)
            {
                if (!partyRestored || (!ReferenceEquals(restoredPlayer, player) && !registrationReplaced))
'@ -replace "`r`n", "`n"

$successorReplacement = @'
            if (heroExists && heroIsDead)
            {
                // A dead Hero is terminal for that campaign character, but the persistent controller
                // identity must remain usable. Drop only the obsolete registration, tell the other
                // clients to release that controller/Hero binding, and route this same admitted
                // connection through normal character creation for an explicit successor.
                Logger.Information(
                    "Controller {ControllerId} hero {HeroId} is dead; releasing the old registration and creating a successor",
                    controllerId,
                    player.HeroId);

                // Removal is an authoritative state transition, not best-effort cleanup. If the
                // registration changed underneath this reconnect (or was already replaced), do not
                // continue into successor creation with a second graph for the same controller.
                if (!playerManager.RemovePlayer(player) || playerManager.TryGetPlayer(controllerId, out _))
                {
                    Logger.Error(
                        "Cannot create successor for controller {ControllerId}: obsolete dead-Hero registration could not be removed cleanly",
                        controllerId);
                    peer.Disconnect();
                    return;
                }

                network.SendAllBut(
                    peer,
                    new GameInterface.Services.Players.Messages.NetworkPlayerRemoved(player.ControllerId, player.HeroId));
                network.SendImmediate(peer, new NetworkClientValidated(false, null));
                ConnectionLogic.CreateCharacter();
                return;
            }

            if (heroExists)
            {
                if (!partyRestored || (!ReferenceEquals(restoredPlayer, player) && !registrationReplaced))
'@ -replace "`r`n", "`n"

if (-not $resolve.Contains($successorAnchor)) {
    throw "Dead-controller successor routing anchor not found in ResolveCharacterState.cs"
}

$resolve = $resolve.Replace($successorAnchor, $successorReplacement)
[IO.File]::WriteAllText($resolvePath, $resolve, [Text.UTF8Encoding]::new($false))

Write-Host 'KaiTOR dead-player recovery, successor routing, and captivity-release lifecycle guards applied successfully.'

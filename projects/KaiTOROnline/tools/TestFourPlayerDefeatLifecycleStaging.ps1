param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'

$restorerPath = Join-Path $UpstreamRoot 'source/GameInterface/Services/Players/PlayerPartyRestorer.cs'
if (-not (Test-Path -LiteralPath $restorerPath)) {
    throw "Missing staged PlayerPartyRestorer.cs: $restorerPath"
}
$restorer = [IO.File]::ReadAllText($restorerPath) -replace "`r`n", "`n"

if ($restorer -notmatch 'if \(hero\.IsDead\)\s*\{\s*return objectManager\.TryGetObjectWithLogging\(player\.MobilePartyId, out MobileParty _\);') {
    throw 'Dead Hero reconnect no longer preserves its registered party for upstream Heir Selection.'
}
if ($restorer -notmatch 'if \(hero\.IsPrisoner \|\| hero\.PartyBelongedToAsPrisoner != null\)') {
    throw 'Prisoner restore path is missing.'
}
if ($restorer -notmatch 'party\.IsActive = false') {
    throw 'Prisoner restore path does not park the player party.'
}
$deadIndex = $restorer.IndexOf('if (hero.IsDead)', [StringComparison]::Ordinal)
$recoveryIndex = $restorer.IndexOf('party = createRecoveryParty(hero);', [StringComparison]::Ordinal)
if ($deadIndex -lt 0 -or $recoveryIndex -lt 0 -or $deadIndex -gt $recoveryIndex) {
    throw 'Dead-player guard must run before any recovery party can be fabricated.'
}

$visibilityPath = Join-Path $UpstreamRoot 'source/Coop.Core/Server/Services/Players/Handlers/PlayerPartyVisibilityHandler.cs'
if (-not (Test-Path -LiteralPath $visibilityPath)) {
    throw "Missing staged PlayerPartyVisibilityHandler.cs: $visibilityPath"
}
$visibility = [IO.File]::ReadAllText($visibilityPath) -replace "`r`n", "`n"

foreach ($marker in @(
    'Handle_PlayerHeirSelectionRequested',
    'Handle_PlayerHeirSelectionCompleted',
    'messageBroker.Publish(this, new PlayerHeirSelectionRequested(hero));',
    'Handle_PlayerPartyReleasedFromCaptivity'
)) {
    if (-not $visibility.Contains($marker)) {
        throw "Defeat lifecycle marker missing: $marker"
    }
}
if ($visibility -notmatch 'objectManager\.TryGetObject\(player\.HeroId, out Hero hero\)') {
    throw 'Captivity release does not re-resolve the authoritative player Hero before party activation.'
}
if ($visibility -notmatch 'hero\.IsDead \|\|\s+hero\.IsPrisoner \|\|\s+hero\.PartyBelongedToAsPrisoner != null') {
    throw 'Captivity release does not reject dead or still-captive Hero state.'
}

$releaseHandlerIndex = $visibility.IndexOf('private void Handle_PlayerPartyReleasedFromCaptivity(', [StringComparison]::Ordinal)
$releaseGuardIndex = $visibility.IndexOf('hero.IsDead ||', $releaseHandlerIndex, [StringComparison]::Ordinal)
$releaseActivateIndex = $visibility.IndexOf('ActivateParty(party, player.MobilePartyId);', $releaseHandlerIndex, [StringComparison]::Ordinal)
if ($releaseHandlerIndex -lt 0 -or $releaseGuardIndex -lt 0 -or $releaseActivateIndex -lt 0 -or $releaseGuardIndex -gt $releaseActivateIndex) {
    throw 'Captivity release Hero-state guard must execute before party activation.'
}

$campaignSyncIndex = $visibility.IndexOf('private void Handle_PlayerCampaignSynchronized(', [StringComparison]::Ordinal)
$deadSyncIndex = $visibility.IndexOf('if (hero.IsDead)', $campaignSyncIndex, [StringComparison]::Ordinal)
$heirRequestIndex = $visibility.IndexOf('messageBroker.Publish(this, new PlayerHeirSelectionRequested(hero));', $deadSyncIndex, [StringComparison]::Ordinal)
$activateAfterSyncIndex = $visibility.IndexOf('ActivateParty(party, player.MobilePartyId);', $campaignSyncIndex, [StringComparison]::Ordinal)
if ($campaignSyncIndex -lt 0 -or $deadSyncIndex -lt 0 -or $heirRequestIndex -lt 0 -or
    $activateAfterSyncIndex -lt 0 -or $deadSyncIndex -gt $heirRequestIndex -or $heirRequestIndex -gt $activateAfterSyncIndex) {
    throw 'Dead reconnect must route to Heir Selection before any party activation.'
}

$resolvePath = Join-Path $UpstreamRoot 'source/Coop.Core/Server/Connections/States/ResolveCharacterState.cs'
if (-not (Test-Path -LiteralPath $resolvePath)) {
    throw "Missing staged ResolveCharacterState.cs: $resolvePath"
}
$resolve = [IO.File]::ReadAllText($resolvePath) -replace "`r`n", "`n"

if ($resolve -notmatch 'partyRestored = playerPartyRestorer\.TryRestore\(player, out restoredPlayer\);') {
    throw 'Reconnect no longer routes persistent players through PlayerPartyRestorer.'
}
foreach ($forbidden in @(
    'var heroIsDead = false;',
    'if (heroExists && heroIsDead)',
    'releasing the old registration and creating a successor'
)) {
    if ($resolve.Contains($forbidden)) {
        throw "Legacy KaiTOR successor fallback is still present: $forbidden"
    }
}

Write-Host 'KaiTOR four-player defeat lifecycle staging: PASS'
Write-Host '  dead Hero -> same persistent registration + party -> upstream Heir Selection'
Write-Host '  no implicit recovery-party resurrection for dead Hero'
Write-Host '  prisoner -> party remains inactive/parked'
Write-Host '  stale release/dead/still-captive Hero -> party remains parked'
Write-Host '  live released Hero -> authoritative party activation remains available'

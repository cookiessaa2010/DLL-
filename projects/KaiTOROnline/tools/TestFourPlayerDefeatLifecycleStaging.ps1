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

if ($restorer -notmatch 'if \(hero\.IsDead\)') {
    throw 'Dead player heroes are not blocked from reconnect/restart party restoration.'
}
if ($restorer -notmatch 'successor handling is required') {
    throw 'Dead-player restore rejection does not expose the explicit successor-policy boundary.'
}
if ($restorer -notmatch 'if \(hero\.IsPrisoner \|\| hero\.PartyBelongedToAsPrisoner != null\)') {
    throw 'Prisoner restore path is missing.'
}
if ($restorer -notmatch 'party\.IsActive = false') {
    throw 'Prisoner restore path does not park the player party.'
}
if ($restorer -notmatch 'if \(party\.LeaderHero != hero\)\s+party\.ChangePartyLeader\(hero\)') {
    throw 'Released/live player restore path does not restore authoritative party leadership.'
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

if ($visibility -notmatch 'Handle_PlayerPartyReleasedFromCaptivity') {
    throw 'Captivity release visibility handler is missing.'
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

Write-Host 'KaiTOR four-player defeat lifecycle staging: PASS'
Write-Host '  dead hero -> no implicit recovery-party resurrection'
Write-Host '  prisoner -> party remains inactive/parked'
Write-Host '  stale release/dead hero -> party remains parked'
Write-Host '  live released hero -> authoritative party activation remains available'

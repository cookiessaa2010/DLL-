param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'

$path = Join-Path $UpstreamRoot 'source/GameInterface/Services/Players/PlayerPartyRestorer.cs'
if (-not (Test-Path -LiteralPath $path)) {
    throw "Missing staged PlayerPartyRestorer.cs: $path"
}

$text = [IO.File]::ReadAllText($path) -replace "`r`n", "`n"

if ($text -notmatch 'if \(hero\.IsDead\)') {
    throw 'Dead player heroes are not blocked from reconnect/restart party restoration.'
}
if ($text -notmatch 'successor handling is required') {
    throw 'Dead-player restore rejection does not expose the explicit successor-policy boundary.'
}
if ($text -notmatch 'if \(hero\.IsPrisoner \|\| hero\.PartyBelongedToAsPrisoner != null\)') {
    throw 'Prisoner restore path is missing.'
}
if ($text -notmatch 'party\.IsActive = false') {
    throw 'Prisoner restore path does not park the player party.'
}
if ($text -notmatch 'if \(party\.LeaderHero != hero\)\s+party\.ChangePartyLeader\(hero\)') {
    throw 'Released/live player restore path does not restore authoritative party leadership.'
}

$deadIndex = $text.IndexOf('if (hero.IsDead)', [StringComparison]::Ordinal)
$recoveryIndex = $text.IndexOf('party = createRecoveryParty(hero);', [StringComparison]::Ordinal)
if ($deadIndex -lt 0 -or $recoveryIndex -lt 0 -or $deadIndex -gt $recoveryIndex) {
    throw 'Dead-player guard must run before any recovery party can be fabricated.'
}

Write-Host 'KaiTOR four-player defeat lifecycle staging: PASS'
Write-Host '  dead hero -> no implicit recovery-party resurrection'
Write-Host '  prisoner -> party remains inactive/parked'
Write-Host '  live/released hero -> authoritative party leadership restored'

param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'

$gatePath = Join-Path $UpstreamRoot 'source/Coop.Core/Server/Connections/PlayerAdmissionGate.cs'
$createPath = Join-Path $UpstreamRoot 'source/Coop.Core/Server/Connections/States/CreateCharacterState.cs'
$logicPath = Join-Path $UpstreamRoot 'source/Coop.Core/Server/Connections/ConnectionLogic.cs'

foreach ($path in @($gatePath, $createPath, $logicPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing staged successor-identity file: $path"
    }
}

$gate = [IO.File]::ReadAllText($gatePath) -replace "`r`n", "`n"
$create = [IO.File]::ReadAllText($createPath) -replace "`r`n", "`n"
$logic = [IO.File]::ReadAllText($logicPath) -replace "`r`n", "`n"

if ($gate -notmatch 'bool TryGetControllerId\(NetPeer peer, out string controllerId\);') {
    throw 'Admission gate does not expose the authoritative controller id for an admitted peer.'
}
if ($gate -notmatch 'peerToController\.TryGetValue\(peer, out controllerId\)') {
    throw 'Admission identity lookup is not backed by the reference-safe peer-to-controller registry.'
}
if ($logic -notmatch 'new CreateCharacterState\([^\r\n]*context\.PlayerAdmissionGate\)') {
    throw 'CreateCharacterState is not constructed with the four-player admission gate.'
}
if ($create -notmatch 'playerAdmissionGate\.TryGetControllerId\(netPeer, out var admittedControllerId\)') {
    throw 'Character creation does not resolve the controller identity from the admitted NetPeer.'
}
if ($create -notmatch 'string\.Equals\(admittedControllerId, obj\.What\.PlayerId, System\.StringComparison\.Ordinal\)') {
    throw 'Character creation does not reject a client-supplied controller id that differs from the admitted identity.'
}
if ($create -notmatch 'var controllerId = admittedControllerId;') {
    throw 'Created Player graph is not forced to use the server-admitted controller identity.'
}

$peerGuard = $create.IndexOf('if (netPeer != ConnectionLogic.Peer) return;', [StringComparison]::Ordinal)
$admissionLookup = $create.IndexOf('playerAdmissionGate.TryGetControllerId(netPeer, out var admittedControllerId)', [StringComparison]::Ordinal)
$identityCompare = $create.IndexOf('string.Equals(admittedControllerId, obj.What.PlayerId, System.StringComparison.Ordinal)', [StringComparison]::Ordinal)
$heroUnpack = $create.IndexOf('heroInterface.ServerUnpackHero(data)', [StringComparison]::Ordinal)
$playerCreate = $create.IndexOf('TryCreatePlayer(controllerId, hero, out var player)', [StringComparison]::Ordinal)

if ($peerGuard -lt 0 -or $admissionLookup -lt 0 -or $identityCompare -lt 0 -or $heroUnpack -lt 0 -or $playerCreate -lt 0 -or
    $peerGuard -gt $admissionLookup -or $admissionLookup -gt $identityCompare -or $identityCompare -gt $heroUnpack -or $heroUnpack -gt $playerCreate) {
    throw 'Authoritative successor identity checks must run before hero unpack/player graph registration.'
}

Write-Host 'KaiTOR four-player successor identity staging: PASS'
Write-Host '  admitted NetPeer -> authoritative controller id'
Write-Host '  mismatched NetworkTransferNewHero PlayerId -> reject/disconnect'
Write-Host '  matched successor -> Player/Hero/Clan/MobileParty graph registered under admitted controller'

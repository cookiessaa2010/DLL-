[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InBattleSnapshotPath,
    [Parameter(Mandatory = $true)][string]$PostBattleSnapshotPath,
    [Parameter(Mandatory = $true)][string]$MovedSnapshotPath,
    [Parameter(Mandatory = $true)][string]$ControllerId,
    [ValidateRange(0.000001,1000.0)][double]$MinimumDistance = 0.01
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Read-Snapshot([string]$Path,[string]$Name) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Name snapshot not found: $Path" }
    try { return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json }
    catch { throw "$Name snapshot is invalid JSON: $($_.Exception.Message)" }
}

function Get-Player($Snapshot,[string]$Name,[string]$Id) {
    $matches = @($Snapshot.players | Where-Object { [string]$_.controllerId -ceq $Id })
    if ($matches.Count -ne 1) { throw "$Name must contain exactly one '$Id'; found $($matches.Count)." }
    $p = $matches[0]
    foreach ($field in @('heroId','mobilePartyId','clanId','characterObjectId','connected','heroResolved','heroIsDead','heroIsPrisoner','partyResolved','partyActive','mapEventId','positionX','positionY')) {
        if ($null -eq $p.PSObject.Properties[$field]) { throw "$Name/$Id is missing '$field'." }
    }
    return $p
}

function Assert-FourOnline($Snapshot,[string]$Name) {
    if (@($Snapshot.players).Count -ne 4 -or [int]$Snapshot.playerRegistryCount -ne 4) { throw "$Name must preserve exactly four player registrations." }
    $connected = @($Snapshot.players | Where-Object { [bool]$_.connected }).Count
    if ($connected -ne 4 -or [int]$Snapshot.activeAdmissionSlots -ne 4) { throw "$Name must have exactly four connected admission slots." }
}

function Assert-SameGraph($Expected,$Actual,[string]$Name,[string]$Id) {
    foreach ($field in @('heroId','mobilePartyId','clanId','characterObjectId')) {
        if ([string]$Actual.$field -cne [string]$Expected.$field) { throw "$Name changed $field for '$Id'." }
    }
}

function Assert-FreeLivingPlayer($Player,[string]$Name,[string]$Id) {
    if (-not [bool]$Player.heroResolved) { throw "$Name/$Id Hero did not resolve." }
    if ([bool]$Player.heroIsDead) { throw "$Name/$Id Hero is dead; four-player post-battle movement cannot be accepted." }
    if ([bool]$Player.heroIsPrisoner) { throw "$Name/$Id Hero is still a prisoner; four-player post-battle movement cannot be accepted." }
    if (-not [bool]$Player.partyResolved -or -not [bool]$Player.partyActive) { throw "$Name/$Id MobileParty is not resolved and active." }
}

function Distance($A,$B) {
    $dx = [double]$B.positionX - [double]$A.positionX
    $dy = [double]$B.positionY - [double]$A.positionY
    return [math]::Sqrt(($dx*$dx)+($dy*$dy))
}

$battle = Read-Snapshot $InBattleSnapshotPath 'in-battle'
$post = Read-Snapshot $PostBattleSnapshotPath 'post-battle'
$moved = Read-Snapshot $MovedSnapshotPath 'moved'
foreach ($phase in @(@{N='in-battle';S=$battle},@{N='post-battle';S=$post},@{N='moved';S=$moved})) { Assert-FourOnline $phase.S $phase.N }

$battlePlayer = Get-Player $battle 'in-battle' $ControllerId
$postPlayer = Get-Player $post 'post-battle' $ControllerId
$movedPlayer = Get-Player $moved 'moved' $ControllerId
Assert-SameGraph $battlePlayer $postPlayer 'post-battle' $ControllerId
Assert-SameGraph $battlePlayer $movedPlayer 'moved' $ControllerId

$battleEvent = [string]$battlePlayer.mapEventId
if ([string]::IsNullOrWhiteSpace($battleEvent)) { throw 'In-battle player must expose a MapEvent id.' }
if (-not [string]::IsNullOrWhiteSpace([string]$postPlayer.mapEventId)) { throw 'Post-battle player retained stale MapEvent ownership.' }
if (-not [string]::IsNullOrWhiteSpace([string]$movedPlayer.mapEventId)) { throw 'Moved player re-entered stale MapEvent ownership.' }
Assert-FreeLivingPlayer $postPlayer 'post-battle' $ControllerId
Assert-FreeLivingPlayer $movedPlayer 'moved' $ControllerId
if ((Distance $postPlayer $movedPlayer) -lt $MinimumDistance) { throw 'Released post-battle party did not resume authoritative map movement.' }

$postById = @{}; foreach ($p in @($post.players)) { $postById[[string]$p.controllerId] = $p }
$vectors = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($p in @($moved.players)) {
    $id = [string]$p.controllerId
    if (-not $postById.ContainsKey($id)) { throw "Moved snapshot introduced unknown controller '$id'." }
    $before = $postById[$id]
    Assert-SameGraph $before $p 'moved' $id
    Assert-FreeLivingPlayer $before 'post-battle' $id
    Assert-FreeLivingPlayer $p 'moved' $id
    if (-not [string]::IsNullOrWhiteSpace([string]$before.mapEventId)) { throw "Post-battle/$id unexpectedly owns MapEvent '$($before.mapEventId)'." }
    if (-not [string]::IsNullOrWhiteSpace([string]$p.mapEventId)) { throw "Moved/$id unexpectedly owns MapEvent '$($p.mapEventId)'." }
    $dx = [double]$p.positionX - [double]$before.positionX
    $dy = [double]$p.positionY - [double]$before.positionY
    $distance = [math]::Sqrt(($dx*$dx)+($dy*$dy))
    if ($distance -lt $MinimumDistance) { throw "Moved/$id did not move far enough: $distance." }
    [void]$vectors.Add(('{0:R},{1:R}' -f $dx,$dy))
}
if ($vectors.Count -lt 2) { throw 'Post-battle movement was not independent; all four parties used the same vector.' }

Write-Output 'KaiTOR four-player post-battle movement: PASS'
Write-Output "  Battle MapEvent cleared: $battleEvent"
Write-Output '  All four Heroes are resolved, alive, free, and backed by active MobileParty instances'
Write-Output '  Reactivated party resumed movement without stale battle ownership'
Write-Output "  Distinct movement vectors: $($vectors.Count)"
Write-Output '  Four-player admission gate remained 4/4'

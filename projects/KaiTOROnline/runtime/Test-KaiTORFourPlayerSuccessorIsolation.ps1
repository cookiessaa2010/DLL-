[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BeforeDeathSnapshotPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$AfterSuccessorSnapshotPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$SuccessorControllerId,

    [ValidateRange(0.0, 10.0)]
    [double]$PositionTolerance = 0.01
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$singleSnapshotValidator = Join-Path $PSScriptRoot 'Test-KaiTORFourPlayerSnapshot.ps1'
if (-not (Test-Path -LiteralPath $singleSnapshotValidator -PathType Leaf)) {
    throw "KaiTOR single-snapshot validator not found: $singleSnapshotValidator"
}

function Read-Snapshot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Name snapshot not found: $Path"
    }

    try {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    catch {
        throw "$Name snapshot is invalid JSON: $($_.Exception.Message)"
    }
}

function Get-PlayerMap {
    param(
        [Parameter(Mandatory = $true)]$Snapshot,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $map = @{}
    foreach ($player in @($Snapshot.players)) {
        $controllerId = [string]$player.controllerId
        if ([string]::IsNullOrWhiteSpace($controllerId)) {
            throw "$Name contains a blank controllerId."
        }
        if ($map.ContainsKey($controllerId)) {
            throw "$Name contains duplicate controllerId '$controllerId'."
        }

        foreach ($field in @('heroId', 'mobilePartyId', 'clanId', 'characterObjectId', 'positionX', 'positionY', 'connected')) {
            if ($null -eq $player.PSObject.Properties[$field]) {
                throw "$Name player '$controllerId' is missing '$field'."
            }
        }

        $map[$controllerId] = [pscustomobject]@{
            heroId = [string]$player.heroId
            mobilePartyId = [string]$player.mobilePartyId
            clanId = [string]$player.clanId
            characterObjectId = [string]$player.characterObjectId
            connected = [bool]$player.connected
            positionX = [double]$player.positionX
            positionY = [double]$player.positionY
        }
    }

    return $map
}

& $singleSnapshotValidator -SnapshotPath $BeforeDeathSnapshotPath -Phase 'four-online'
& $singleSnapshotValidator -SnapshotPath $AfterSuccessorSnapshotPath -Phase 'four-online'

$before = Read-Snapshot -Path $BeforeDeathSnapshotPath -Name 'before-death'
$after = Read-Snapshot -Path $AfterSuccessorSnapshotPath -Name 'after-successor'
$beforeMap = Get-PlayerMap -Snapshot $before -Name 'before-death'
$afterMap = Get-PlayerMap -Snapshot $after -Name 'after-successor'

if ($beforeMap.Count -ne 4 -or $afterMap.Count -ne 4) {
    throw "Successor isolation requires exactly four persistent controllers before and after; before=$($beforeMap.Count), after=$($afterMap.Count)."
}
if (-not $beforeMap.ContainsKey($SuccessorControllerId)) {
    throw "Successor controller '$SuccessorControllerId' is absent from before-death snapshot."
}
if (-not $afterMap.ContainsKey($SuccessorControllerId)) {
    throw "Successor controller '$SuccessorControllerId' is absent from after-successor snapshot."
}

# The controller identity owns the admission slot and must survive character succession.
# The dead Hero, its MobileParty, and CharacterObject must never be silently reused as the
# successor's active player graph. Clan identity is intentionally not required to change here:
# Bannerlord/TOR succession policy may preserve or replace the clan, but it still must remain
# isolated from the other three controllers.
$oldSuccessor = $beforeMap[$SuccessorControllerId]
$newSuccessor = $afterMap[$SuccessorControllerId]
foreach ($field in @('heroId', 'mobilePartyId', 'characterObjectId')) {
    $oldId = [string]$oldSuccessor.$field
    $newId = [string]$newSuccessor.$field
    if ($newId -ceq $oldId) {
        throw "Successor controller '$SuccessorControllerId' reused dead $field '$oldId'."
    }
}

# The other three player graphs are immutable across somebody else's death/succession.
foreach ($controllerId in $beforeMap.Keys) {
    if ($controllerId -ceq $SuccessorControllerId) { continue }
    if (-not $afterMap.ContainsKey($controllerId)) {
        throw "Successor transition lost unaffected controller '$controllerId'."
    }

    foreach ($field in @('heroId', 'mobilePartyId', 'clanId', 'characterObjectId')) {
        $expected = [string]$beforeMap[$controllerId].$field
        $actual = [string]$afterMap[$controllerId].$field
        if ($actual -cne $expected) {
            throw "Successor transition changed unaffected $field for '$controllerId': '$expected' -> '$actual'."
        }
    }

    $dx = [math]::Abs([double]$afterMap[$controllerId].positionX - [double]$beforeMap[$controllerId].positionX)
    $dy = [math]::Abs([double]$afterMap[$controllerId].positionY - [double]$beforeMap[$controllerId].positionY)
    if ($dx -gt $PositionTolerance -or $dy -gt $PositionTolerance) {
        throw "Successor transition moved unaffected controller '$controllerId': dx=$dx dy=$dy tolerance=$PositionTolerance."
    }
}

# No ID in the successor graph may alias any currently registered graph owned by another
# controller. This catches stale ownership markers / orphan reuse at the observable runtime level.
foreach ($field in @('heroId', 'mobilePartyId', 'clanId', 'characterObjectId')) {
    $successorId = [string]$newSuccessor.$field
    foreach ($controllerId in $afterMap.Keys) {
        if ($controllerId -ceq $SuccessorControllerId) { continue }
        if ($successorId -ceq [string]$afterMap[$controllerId].$field) {
            throw "Successor $field '$successorId' aliases controller '$controllerId'."
        }
    }
}

if (-not $newSuccessor.connected) {
    throw "Successor controller '$SuccessorControllerId' must be connected after successor creation."
}

Write-Output 'KaiTOR four-player successor isolation: PASS'
Write-Output "  Successor controller: $SuccessorControllerId"
Write-Output '  Dead Hero/MobileParty/CharacterObject: not reused'
Write-Output '  Other three player graphs: preserved'
Write-Output "  Other three authoritative positions: preserved within tolerance $PositionTolerance"
Write-Output '  Successor graph: isolated from every other registered controller'
Write-Output '  Clan policy: inheritance or replacement allowed, cross-controller aliasing forbidden'

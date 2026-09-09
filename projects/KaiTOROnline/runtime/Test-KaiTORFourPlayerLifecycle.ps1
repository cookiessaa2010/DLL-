[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$FourOnlineSnapshotPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$SlotFreedSnapshotPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ReconnectedSnapshotPath,

    [ValidateNotNullOrEmpty()]
    [string]$RestartedSnapshotPath
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

function Get-IdentityMap {
    param(
        [Parameter(Mandatory = $true)]$Snapshot,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $map = @{}
    foreach ($player in @($Snapshot.players)) {
        $controllerId = [string]$player.controllerId
        if ([string]::IsNullOrWhiteSpace($controllerId)) {
            throw "$Name snapshot contains a blank controllerId."
        }
        if ($map.ContainsKey($controllerId)) {
            throw "$Name snapshot contains duplicate controllerId '$controllerId'."
        }

        $map[$controllerId] = [pscustomobject]@{
            heroId = [string]$player.heroId
            mobilePartyId = [string]$player.mobilePartyId
            clanId = [string]$player.clanId
            characterObjectId = [string]$player.characterObjectId
        }
    }
    return $map
}

function Assert-SameIdentityGraph {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Baseline,
        [Parameter(Mandatory = $true)][hashtable]$Candidate,
        [Parameter(Mandatory = $true)][string]$CandidateName
    )

    if ($Candidate.Count -lt $Baseline.Count) {
        throw "$CandidateName lost persistent player registrations: baseline=$($Baseline.Count), candidate=$($Candidate.Count)."
    }

    foreach ($controllerId in $Baseline.Keys) {
        if (-not $Candidate.ContainsKey($controllerId)) {
            throw "$CandidateName lost controller registration '$controllerId'."
        }

        foreach ($field in @('heroId', 'mobilePartyId', 'clanId', 'characterObjectId')) {
            $expected = [string]$Baseline[$controllerId].$field
            $actual = [string]$Candidate[$controllerId].$field
            if ($actual -cne $expected) {
                throw "$CandidateName changed $field for controller '$controllerId': '$expected' -> '$actual'."
            }
        }
    }
}

& $singleSnapshotValidator -SnapshotPath $FourOnlineSnapshotPath -Phase 'four-online'
& $singleSnapshotValidator -SnapshotPath $SlotFreedSnapshotPath -Phase 'slot-freed'
& $singleSnapshotValidator -SnapshotPath $ReconnectedSnapshotPath -Phase 'four-online'

$online = Read-Snapshot -Path $FourOnlineSnapshotPath -Name 'four-online'
$freed = Read-Snapshot -Path $SlotFreedSnapshotPath -Name 'slot-freed'
$reconnected = Read-Snapshot -Path $ReconnectedSnapshotPath -Name 'reconnected'

$baselineMap = Get-IdentityMap -Snapshot $online -Name 'four-online'
Assert-SameIdentityGraph -Baseline $baselineMap -Candidate (Get-IdentityMap -Snapshot $freed -Name 'slot-freed') -CandidateName 'slot-freed'
Assert-SameIdentityGraph -Baseline $baselineMap -Candidate (Get-IdentityMap -Snapshot $reconnected -Name 'reconnected') -CandidateName 'reconnected'

if (-not [string]::IsNullOrWhiteSpace($RestartedSnapshotPath)) {
    & $singleSnapshotValidator -SnapshotPath $RestartedSnapshotPath -Phase 'four-online'

    $restarted = Read-Snapshot -Path $RestartedSnapshotPath -Name 'restarted'
    Assert-SameIdentityGraph -Baseline $baselineMap -Candidate (Get-IdentityMap -Snapshot $restarted -Name 'restarted') -CandidateName 'restarted'
}

Write-Output 'KaiTOR four-player lifecycle: PASS'
Write-Output "  Persistent identity graphs: $($baselineMap.Count)"
Write-Output '  four-online -> slot-freed: preserved'
Write-Output '  slot-freed -> reconnected: preserved'
if (-not [string]::IsNullOrWhiteSpace($RestartedSnapshotPath)) {
    Write-Output '  reconnected -> restarted: preserved'
}

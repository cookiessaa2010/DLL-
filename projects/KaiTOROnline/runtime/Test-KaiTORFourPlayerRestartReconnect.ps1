[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BeforeRestartSnapshotPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$AfterRestartSnapshotPath,

    [Parameter(Mandatory = $true)]
    [ValidateCount(4, 4)]
    [string[]]$ReconnectSnapshotPaths,

    [Parameter(Mandatory = $true)]
    [ValidateCount(4, 4)]
    [string[]]$ExpectedReconnectOrder,

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

        $x = [double]$player.positionX
        $y = [double]$player.positionY
        if ([double]::IsNaN($x) -or [double]::IsInfinity($x) -or [double]::IsNaN($y) -or [double]::IsInfinity($y)) {
            throw "$Name player '$controllerId' has a non-finite authoritative position."
        }

        $map[$controllerId] = [pscustomobject]@{
            heroId = [string]$player.heroId
            mobilePartyId = [string]$player.mobilePartyId
            clanId = [string]$player.clanId
            characterObjectId = [string]$player.characterObjectId
            connected = [bool]$player.connected
            positionX = $x
            positionY = $y
        }
    }

    return $map
}

function Assert-PersistentState {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Baseline,
        [Parameter(Mandatory = $true)][hashtable]$Candidate,
        [Parameter(Mandatory = $true)][string]$CandidateName
    )

    if ($Candidate.Count -ne $Baseline.Count) {
        throw "$CandidateName changed persistent player count: baseline=$($Baseline.Count), candidate=$($Candidate.Count)."
    }

    foreach ($controllerId in $Baseline.Keys) {
        if (-not $Candidate.ContainsKey($controllerId)) {
            throw "$CandidateName lost controller '$controllerId'."
        }

        foreach ($field in @('heroId', 'mobilePartyId', 'clanId', 'characterObjectId')) {
            $expected = [string]$Baseline[$controllerId].$field
            $actual = [string]$Candidate[$controllerId].$field
            if ($actual -cne $expected) {
                throw "$CandidateName changed $field for '$controllerId': '$expected' -> '$actual'."
            }
        }

        $dx = [math]::Abs([double]$Candidate[$controllerId].positionX - [double]$Baseline[$controllerId].positionX)
        $dy = [math]::Abs([double]$Candidate[$controllerId].positionY - [double]$Baseline[$controllerId].positionY)
        if ($dx -gt $PositionTolerance -or $dy -gt $PositionTolerance) {
            throw "$CandidateName changed authoritative position for '$controllerId' across restart/reconnect: dx=$dx dy=$dy tolerance=$PositionTolerance."
        }
    }
}

function Get-ConnectedControllers {
    param([Parameter(Mandatory = $true)][hashtable]$PlayerMap)

    return @($PlayerMap.Keys | Where-Object { $PlayerMap[$_].connected } | Sort-Object)
}

& $singleSnapshotValidator -SnapshotPath $BeforeRestartSnapshotPath -Phase 'four-online'
& $singleSnapshotValidator -SnapshotPath $AfterRestartSnapshotPath -Phase 'slot-freed'

$before = Read-Snapshot -Path $BeforeRestartSnapshotPath -Name 'before-restart'
$afterRestart = Read-Snapshot -Path $AfterRestartSnapshotPath -Name 'after-restart'
$baselineMap = Get-PlayerMap -Snapshot $before -Name 'before-restart'
$afterRestartMap = Get-PlayerMap -Snapshot $afterRestart -Name 'after-restart'

if ($baselineMap.Count -ne 4) {
    throw "Restart/reconnect acceptance requires exactly four persistent controllers; found $($baselineMap.Count)."
}

$orderSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($controllerId in $ExpectedReconnectOrder) {
    if ([string]::IsNullOrWhiteSpace($controllerId)) {
        throw 'ExpectedReconnectOrder cannot contain blank controller ids.'
    }
    if (-not $orderSet.Add($controllerId)) {
        throw "ExpectedReconnectOrder contains duplicate controller '$controllerId'."
    }
    if (-not $baselineMap.ContainsKey($controllerId)) {
        throw "ExpectedReconnectOrder contains unknown controller '$controllerId'."
    }
}

Assert-PersistentState -Baseline $baselineMap -Candidate $afterRestartMap -CandidateName 'after-restart'
$connectedAfterRestart = @(Get-ConnectedControllers -PlayerMap $afterRestartMap)
if ($connectedAfterRestart.Count -ne 0) {
    throw "after-restart must begin with zero live peers; connected=$($connectedAfterRestart -join ',')."
}

for ($i = 0; $i -lt 4; $i++) {
    $phase = if ($i -eq 3) { 'four-online' } else { 'slot-freed' }
    $name = "reconnect-$($i + 1)"
    $path = $ReconnectSnapshotPaths[$i]

    & $singleSnapshotValidator -SnapshotPath $path -Phase $phase
    $snapshot = Read-Snapshot -Path $path -Name $name
    $candidateMap = Get-PlayerMap -Snapshot $snapshot -Name $name
    Assert-PersistentState -Baseline $baselineMap -Candidate $candidateMap -CandidateName $name

    $actualConnected = @(Get-ConnectedControllers -PlayerMap $candidateMap)
    $expectedConnected = @($ExpectedReconnectOrder[0..$i] | Sort-Object)

    if ($actualConnected.Count -ne $expectedConnected.Count) {
        throw "$name has wrong live peer count: expected=$($expectedConnected.Count), actual=$($actualConnected.Count)."
    }

    for ($j = 0; $j -lt $expectedConnected.Count; $j++) {
        if ($actualConnected[$j] -cne $expectedConnected[$j]) {
            throw "$name connected controller set differs from expected reconnect prefix. Expected='$($expectedConnected -join ',')' Actual='$($actualConnected -join ',')'."
        }
    }
}

Write-Output 'KaiTOR four-player restart/reconnect state: PASS'
Write-Output '  Persistent Hero/Clan/MobileParty/Character graph: preserved'
Write-Output "  Authoritative party positions: preserved within tolerance $PositionTolerance"
Write-Output "  Reconnect order: $($ExpectedReconnectOrder -join ' -> ')"
Write-Output '  Live admission progression: 0 -> 1 -> 2 -> 3 -> 4'

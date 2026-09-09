[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BeforeSnapshotPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$AfterSnapshotPath,

    [ValidateRange(0.000001, 1000.0)]
    [double]$MinimumDistance = 0.01
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

function Get-RequiredProperty {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Context
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "$Context is missing required field '$Name'."
    }
    return $property.Value
}

function Convert-ToFiniteDouble {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Context
    )

    try {
        $number = [double]$Value
    }
    catch {
        throw "$Context is not numeric: '$Value'."
    }

    if ([double]::IsNaN($number) -or [double]::IsInfinity($number)) {
        throw "$Context must be finite; found '$Value'."
    }
    return $number
}

function Get-PartyStateMap {
    param(
        [Parameter(Mandatory = $true)]$Snapshot,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $map = @{}
    foreach ($player in @($Snapshot.players)) {
        $controllerId = [string](Get-RequiredProperty -Object $player -Name 'controllerId' -Context "$Name player")
        if ([string]::IsNullOrWhiteSpace($controllerId)) {
            throw "$Name snapshot contains a blank controllerId."
        }
        if ($map.ContainsKey($controllerId)) {
            throw "$Name snapshot contains duplicate controllerId '$controllerId'."
        }

        $partyResolved = [bool](Get-RequiredProperty -Object $player -Name 'partyResolved' -Context "$Name/$controllerId")
        $partyActive = [bool](Get-RequiredProperty -Object $player -Name 'partyActive' -Context "$Name/$controllerId")
        if (-not $partyResolved) {
            throw "$Name/$controllerId MobileParty is not resolved by the authoritative server."
        }
        if (-not $partyActive) {
            throw "$Name/$controllerId MobileParty is not active on the campaign map."
        }

        $x = Convert-ToFiniteDouble -Value (Get-RequiredProperty -Object $player -Name 'positionX' -Context "$Name/$controllerId") -Context "$Name/$controllerId.positionX"
        $y = Convert-ToFiniteDouble -Value (Get-RequiredProperty -Object $player -Name 'positionY' -Context "$Name/$controllerId") -Context "$Name/$controllerId.positionY"

        $map[$controllerId] = [pscustomobject]@{
            heroId = [string](Get-RequiredProperty -Object $player -Name 'heroId' -Context "$Name/$controllerId")
            mobilePartyId = [string](Get-RequiredProperty -Object $player -Name 'mobilePartyId' -Context "$Name/$controllerId")
            clanId = [string](Get-RequiredProperty -Object $player -Name 'clanId' -Context "$Name/$controllerId")
            characterObjectId = [string](Get-RequiredProperty -Object $player -Name 'characterObjectId' -Context "$Name/$controllerId")
            x = $x
            y = $y
        }
    }
    return $map
}

& $singleSnapshotValidator -SnapshotPath $BeforeSnapshotPath -Phase 'four-online'
& $singleSnapshotValidator -SnapshotPath $AfterSnapshotPath -Phase 'four-online'

$before = Read-Snapshot -Path $BeforeSnapshotPath -Name 'before'
$after = Read-Snapshot -Path $AfterSnapshotPath -Name 'after'
$beforeMap = Get-PartyStateMap -Snapshot $before -Name 'before'
$afterMap = Get-PartyStateMap -Snapshot $after -Name 'after'

if ($beforeMap.Count -ne 4 -or $afterMap.Count -ne 4) {
    throw "Movement acceptance requires exactly four persistent controller graphs in both snapshots; before=$($beforeMap.Count), after=$($afterMap.Count)."
}

$movedCount = 0
$movementVectors = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($controllerId in $beforeMap.Keys) {
    if (-not $afterMap.ContainsKey($controllerId)) {
        throw "After snapshot lost controller '$controllerId'."
    }

    $beforeParty = $beforeMap[$controllerId]
    $afterParty = $afterMap[$controllerId]
    foreach ($field in @('heroId', 'mobilePartyId', 'clanId', 'characterObjectId')) {
        if ([string]$afterParty.$field -cne [string]$beforeParty.$field) {
            throw "Controller '$controllerId' changed $field during movement: '$($beforeParty.$field)' -> '$($afterParty.$field)'."
        }
    }

    $dx = $afterParty.x - $beforeParty.x
    $dy = $afterParty.y - $beforeParty.y
    $distance = [math]::Sqrt(($dx * $dx) + ($dy * $dy))
    if ($distance -lt $MinimumDistance) {
        throw "Controller '$controllerId' MobileParty did not move far enough: distance=$distance, minimum=$MinimumDistance."
    }

    $movedCount++
    [void]$movementVectors.Add(('{0:R},{1:R}' -f $dx, $dy))
}

if ($movedCount -ne 4) {
    throw "Expected all four authoritative MobileParty instances to move; moved=$movedCount."
}
if ($movementVectors.Count -lt 2) {
    throw 'All four MobileParty instances moved with the exact same vector; independent movement was not demonstrated.'
}

Write-Output 'KaiTOR four-player movement: PASS'
Write-Output "  Authoritative parties moved: $movedCount/4"
Write-Output "  Distinct movement vectors:   $($movementVectors.Count)"
Write-Output "  Minimum accepted distance:   $MinimumDistance"
Write-Output '  Persistent identity graph:   preserved'

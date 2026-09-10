[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BeforeCaptureSnapshotPath,
    [Parameter(Mandatory = $true)][string]$CaptiveSnapshotPath,
    [Parameter(Mandatory = $true)][string]$ReleasedSnapshotPath,
    [Parameter(Mandatory = $true)][string]$ControllerId
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Read-Snapshot([string]$Path, [string]$Name) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Name snapshot not found: $Path" }
    try { return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json }
    catch { throw "$Name snapshot is invalid JSON: $($_.Exception.Message)" }
}

function Get-Player($Snapshot, [string]$Name, [string]$Id) {
    $matches = @($Snapshot.players | Where-Object { [string]$_.controllerId -ceq $Id })
    if ($matches.Count -ne 1) { throw "$Name must contain exactly one '$Id' registration; found $($matches.Count)." }
    $player = $matches[0]
    foreach ($field in @('heroId','mobilePartyId','clanId','characterObjectId','connected','heroResolved','heroIsDead','heroIsPrisoner','prisonerPartyId','partyResolved','partyActive')) {
        if ($null -eq $player.PSObject.Properties[$field]) { throw "$Name player '$Id' is missing '$field'." }
    }
    return $player
}

$before = Read-Snapshot $BeforeCaptureSnapshotPath 'before-capture'
$captive = Read-Snapshot $CaptiveSnapshotPath 'captive'
$released = Read-Snapshot $ReleasedSnapshotPath 'released'

$beforePlayer = Get-Player $before 'before-capture' $ControllerId
$captivePlayer = Get-Player $captive 'captive' $ControllerId
$releasedPlayer = Get-Player $released 'released' $ControllerId

foreach ($snapshot in @($before,$captive,$released)) {
    if (@($snapshot.players).Count -ne 4 -or [int]$snapshot.playerRegistryCount -ne 4) {
        throw 'Captivity lifecycle requires four persistent player registrations throughout the transition.'
    }
}

# Captivity is a state transition of the same authoritative player graph, not a respawn.
foreach ($field in @('heroId','mobilePartyId','clanId','characterObjectId')) {
    $expected = [string]$beforePlayer.$field
    if ([string]$captivePlayer.$field -cne $expected) { throw "Capture changed $field for '$ControllerId'." }
    if ([string]$releasedPlayer.$field -cne $expected) { throw "Release changed $field for '$ControllerId'." }
}

if (-not [bool]$beforePlayer.heroResolved -or [bool]$beforePlayer.heroIsDead -or [bool]$beforePlayer.heroIsPrisoner) {
    throw 'Before-capture Hero must resolve, be alive, and not be a prisoner.'
}
if (-not [bool]$captivePlayer.heroResolved -or [bool]$captivePlayer.heroIsDead -or -not [bool]$captivePlayer.heroIsPrisoner) {
    throw 'Captive Hero must resolve, remain alive, and be marked prisoner.'
}
if ([string]::IsNullOrWhiteSpace([string]$captivePlayer.prisonerPartyId)) {
    throw 'Captive Hero must expose an authoritative prisonerPartyId.'
}
if ([bool]$captivePlayer.partyActive) {
    throw 'Captured player MobileParty must remain parked/inactive while Hero is prisoner.'
}

if (-not [bool]$releasedPlayer.heroResolved -or [bool]$releasedPlayer.heroIsDead -or [bool]$releasedPlayer.heroIsPrisoner) {
    throw 'Released Hero must resolve, remain alive, and no longer be a prisoner.'
}
if (-not [string]::IsNullOrWhiteSpace([string]$releasedPlayer.prisonerPartyId)) {
    throw 'Released Hero still has prisonerPartyId; stale captivity state would make activation unsafe.'
}
if (-not [bool]$releasedPlayer.partyResolved -or -not [bool]$releasedPlayer.partyActive) {
    throw 'Released player MobileParty must resolve and reactivate after authoritative release.'
}

# Other controllers must keep their identity graphs across somebody else's capture/release.
$beforeByController = @{}
foreach ($p in @($before.players)) { $beforeByController[[string]$p.controllerId] = $p }
foreach ($phase in @(@{ Name='captive'; Snapshot=$captive }, @{ Name='released'; Snapshot=$released })) {
    foreach ($p in @($phase.Snapshot.players)) {
        $id = [string]$p.controllerId
        if ($id -ceq $ControllerId) { continue }
        if (-not $beforeByController.ContainsKey($id)) { throw "$($phase.Name) introduced unknown controller '$id'." }
        foreach ($field in @('heroId','mobilePartyId','clanId','characterObjectId')) {
            if ([string]$p.$field -cne [string]$beforeByController[$id].$field) {
                throw "$($phase.Name) changed unaffected $field for '$id'."
            }
        }
    }
}

Write-Output 'KaiTOR four-player captivity lifecycle: PASS'
Write-Output "  Controller: $ControllerId"
Write-Output '  Capture: same graph, Hero prisoner, MobileParty parked'
Write-Output '  Release: captivity cleared before MobileParty activation'
Write-Output '  Other three player identity graphs: preserved'

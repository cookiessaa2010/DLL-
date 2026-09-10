[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BeforeCaptureSnapshotPath,
    [Parameter(Mandatory = $true)][string]$DisconnectedCaptiveSnapshotPath,
    [Parameter(Mandatory = $true)][string]$RestartedCaptiveSnapshotPath,
    [Parameter(Mandatory = $true)][string]$ReconnectedCaptiveSnapshotPath,
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

function Assert-FourPersistentRegistrations($Snapshot, [string]$Name) {
    if (@($Snapshot.players).Count -ne 4 -or [int]$Snapshot.playerRegistryCount -ne 4) {
        throw "$Name must preserve exactly four persistent player registrations."
    }
    if ([int]$Snapshot.activeAdmissionSlots -gt 4) {
        throw "$Name exceeds the four-player admission limit."
    }
}

function Assert-SameGraph($Expected, $Actual, [string]$Name, [string]$Id) {
    foreach ($field in @('heroId','mobilePartyId','clanId','characterObjectId')) {
        if ([string]$Actual.$field -cne [string]$Expected.$field) {
            throw "$Name changed $field for '$Id'."
        }
    }
}

function Assert-Captive($Player, [string]$Name, [bool]$ExpectedConnected) {
    if ([bool]$Player.connected -ne $ExpectedConnected) {
        throw "$Name connected state is invalid for '$ControllerId'."
    }
    if (-not [bool]$Player.heroResolved -or [bool]$Player.heroIsDead -or -not [bool]$Player.heroIsPrisoner) {
        throw "$Name must keep '$ControllerId' as a living authoritative prisoner."
    }
    if ([string]::IsNullOrWhiteSpace([string]$Player.prisonerPartyId)) {
        throw "$Name lost authoritative prisonerPartyId for '$ControllerId'."
    }
    if ([bool]$Player.partyActive) {
        throw "$Name prematurely activated '$ControllerId' MobileParty while Hero is prisoner."
    }
}

$before = Read-Snapshot $BeforeCaptureSnapshotPath 'before-capture'
$disconnected = Read-Snapshot $DisconnectedCaptiveSnapshotPath 'disconnected-captive'
$restarted = Read-Snapshot $RestartedCaptiveSnapshotPath 'restarted-captive'
$reconnected = Read-Snapshot $ReconnectedCaptiveSnapshotPath 'reconnected-captive'
$released = Read-Snapshot $ReleasedSnapshotPath 'released'

$phases = @(
    @{ Name='before-capture'; Snapshot=$before },
    @{ Name='disconnected-captive'; Snapshot=$disconnected },
    @{ Name='restarted-captive'; Snapshot=$restarted },
    @{ Name='reconnected-captive'; Snapshot=$reconnected },
    @{ Name='released'; Snapshot=$released }
)
foreach ($phase in $phases) { Assert-FourPersistentRegistrations $phase.Snapshot $phase.Name }

$beforePlayer = Get-Player $before 'before-capture' $ControllerId
$disconnectedPlayer = Get-Player $disconnected 'disconnected-captive' $ControllerId
$restartedPlayer = Get-Player $restarted 'restarted-captive' $ControllerId
$reconnectedPlayer = Get-Player $reconnected 'reconnected-captive' $ControllerId
$releasedPlayer = Get-Player $released 'released' $ControllerId

if (-not [bool]$beforePlayer.connected -or -not [bool]$beforePlayer.heroResolved -or [bool]$beforePlayer.heroIsDead -or [bool]$beforePlayer.heroIsPrisoner) {
    throw 'Before-capture player must be connected, alive, resolved, and free.'
}

foreach ($entry in @(
    @{ Name='disconnected-captive'; Player=$disconnectedPlayer },
    @{ Name='restarted-captive'; Player=$restartedPlayer },
    @{ Name='reconnected-captive'; Player=$reconnectedPlayer },
    @{ Name='released'; Player=$releasedPlayer }
)) {
    Assert-SameGraph $beforePlayer $entry.Player $entry.Name $ControllerId
}

Assert-Captive $disconnectedPlayer 'disconnected-captive' $false
Assert-Captive $restartedPlayer 'restarted-captive' $false
Assert-Captive $reconnectedPlayer 'reconnected-captive' $true

$prisonerPartyId = [string]$disconnectedPlayer.prisonerPartyId
foreach ($entry in @(
    @{ Name='restarted-captive'; Player=$restartedPlayer },
    @{ Name='reconnected-captive'; Player=$reconnectedPlayer }
)) {
    if ([string]$entry.Player.prisonerPartyId -cne $prisonerPartyId) {
        throw "$($entry.Name) changed prisonerPartyId for '$ControllerId' across restart/reconnect."
    }
}

if (-not [bool]$releasedPlayer.connected -or -not [bool]$releasedPlayer.heroResolved -or [bool]$releasedPlayer.heroIsDead -or [bool]$releasedPlayer.heroIsPrisoner) {
    throw 'Released player must remain connected/alive and have captivity cleared.'
}
if (-not [string]::IsNullOrWhiteSpace([string]$releasedPlayer.prisonerPartyId)) {
    throw 'Released player retained stale prisonerPartyId.'
}
if (-not [bool]$releasedPlayer.partyResolved -or -not [bool]$releasedPlayer.partyActive) {
    throw 'Released player MobileParty must reactivate only after captivity is cleared.'
}

$beforeByController = @{}
foreach ($p in @($before.players)) { $beforeByController[[string]$p.controllerId] = $p }
foreach ($phase in @($disconnected,$restarted,$reconnected,$released)) {
    foreach ($p in @($phase.players)) {
        $id = [string]$p.controllerId
        if ($id -ceq $ControllerId) { continue }
        if (-not $beforeByController.ContainsKey($id)) { throw "Lifecycle introduced unknown controller '$id'." }
        Assert-SameGraph $beforeByController[$id] $p 'unaffected-player lifecycle' $id
    }
}

Write-Output 'KaiTOR four-player captivity restart/reconnect lifecycle: PASS'
Write-Output "  Controller: $ControllerId"
Write-Output '  Captivity persisted through disconnect, save/restart, and reconnect'
Write-Output '  Captive MobileParty remained inactive until authoritative release'
Write-Output '  Four-player registration cap and unaffected player graphs were preserved'

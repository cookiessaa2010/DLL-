[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$SnapshotPath,

    [ValidateSet('four-online', 'slot-freed')]
    [string]$Phase = 'four-online'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path -LiteralPath $SnapshotPath -PathType Leaf)) {
    throw "KaiTOR runtime snapshot not found: $SnapshotPath"
}

try {
    $snapshot = Get-Content -LiteralPath $SnapshotPath -Raw | ConvertFrom-Json
}
catch {
    throw "KaiTOR runtime snapshot is not valid JSON: $($_.Exception.Message)"
}

if ($null -eq $snapshot.players) {
    throw "Snapshot must contain a 'players' array."
}

$players = @($snapshot.players)
if ($players.Count -lt 4) {
    throw "Expected at least four persistent player registrations; found $($players.Count)."
}

function Assert-Field {
    param(
        [Parameter(Mandatory = $true)]$Player,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][int]$Index
    )

    $property = $Player.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "Player[$Index] is missing required field '$Name'."
    }

    $value = [string]$property.Value
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Player[$Index].$Name cannot be blank."
    }

    return $value
}

$controllerIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$heroIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$partyIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$clanIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$characterIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$connectedCount = 0

for ($i = 0; $i -lt $players.Count; $i++) {
    $player = $players[$i]
    $controllerId = Assert-Field -Player $player -Name 'controllerId' -Index $i
    $heroId = Assert-Field -Player $player -Name 'heroId' -Index $i
    $partyId = Assert-Field -Player $player -Name 'mobilePartyId' -Index $i
    $clanId = Assert-Field -Player $player -Name 'clanId' -Index $i
    $characterId = Assert-Field -Player $player -Name 'characterObjectId' -Index $i

    if (-not $controllerIds.Add($controllerId)) { throw "Duplicate controllerId '$controllerId'." }
    if (-not $heroIds.Add($heroId)) { throw "Duplicate heroId '$heroId'." }
    if (-not $partyIds.Add($partyId)) { throw "Duplicate mobilePartyId '$partyId'." }
    if (-not $clanIds.Add($clanId)) { throw "Duplicate clanId '$clanId'." }
    if (-not $characterIds.Add($characterId)) { throw "Duplicate characterObjectId '$characterId'." }

    $connectedProperty = $player.PSObject.Properties['connected']
    if ($null -eq $connectedProperty) {
        throw "Player[$i] is missing required field 'connected'."
    }
    if ([bool]$connectedProperty.Value) { $connectedCount++ }
}

$activeSlotsProperty = $snapshot.PSObject.Properties['activeAdmissionSlots']
if ($null -eq $activeSlotsProperty) {
    throw "Snapshot must contain 'activeAdmissionSlots'."
}
$activeSlots = [int]$activeSlotsProperty.Value
if ($activeSlots -lt 0 -or $activeSlots -gt 4) {
    throw "4-player admission invariant violated: activeAdmissionSlots=$activeSlots."
}
if ($activeSlots -ne $connectedCount) {
    throw "Admission/player mismatch: activeAdmissionSlots=$activeSlots but connected players=$connectedCount."
}

$registryCountProperty = $snapshot.PSObject.Properties['playerRegistryCount']
if ($null -eq $registryCountProperty) {
    throw "Snapshot must contain 'playerRegistryCount'."
}
$registryCount = [int]$registryCountProperty.Value
if ($registryCount -ne $players.Count) {
    throw "Persistent registry mismatch: playerRegistryCount=$registryCount but snapshot has $($players.Count) players."
}

switch ($Phase) {
    'four-online' {
        if ($activeSlots -ne 4) {
            throw "four-online phase requires exactly four active admission slots; found $activeSlots."
        }
    }
    'slot-freed' {
        if ($activeSlots -ge 4) {
            throw "slot-freed phase requires at least one free live slot; found $activeSlots active slots."
        }
        if ($registryCount -lt 4) {
            throw "Disconnect must not delete persistent registrations; found only $registryCount registrations."
        }
    }
}

Write-Output "KaiTOR four-player snapshot: PASS"
Write-Output "  Phase:               $Phase"
Write-Output "  Persistent players:  $registryCount"
Write-Output "  Connected players:   $connectedCount"
Write-Output "  Admission slots:      $activeSlots/4"
Write-Output "  Unique Hero graphs:   $($heroIds.Count)"
Write-Output "  Unique MobileParties: $($partyIds.Count)"

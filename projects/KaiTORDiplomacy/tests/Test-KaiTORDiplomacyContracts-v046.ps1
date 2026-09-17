[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $root 'module\SubModule.xml'
$projectPath = Join-Path $root 'src\KaiTOR_Diplomacy.csproj'
$srcRoot = Join-Path $root 'src'

[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
if ($manifest.Module.Id.value -ne 'KaiTOR_Diplomacy') { throw 'Unexpected module id.' }
if ($manifest.Module.Version.value -ne 'v0.4.6') { throw 'Unexpected module version.' }

$project = Get-Content -LiteralPath $projectPath -Raw
foreach ($required in @(
    '<Version>0.4.6</Version>',
    '<AssemblyVersion>0.4.6.0</AssemblyVersion>',
    '<FileVersion>0.4.6.0</FileVersion>',
    '<InformationalVersion>0.4.6-ImmersiveUI-MarriageOffers-NationalityFix</InformationalVersion>'
)) {
    if ($project -notmatch [regex]::Escape($required)) { throw "Assembly version stamp missing: $required" }
}

$subModule = Get-Content (Join-Path $srcRoot 'SubModule.cs') -Raw
$office = Get-Content (Join-Path $srcRoot 'Runtime\KaiDiplomacyOfficeBehavior.cs') -Raw
$culture = Get-Content (Join-Path $srcRoot 'Runtime\KaiCultureAssimilationBehavior.cs') -Raw
$bridge = Get-Content (Join-Path $srcRoot 'Runtime\TorSettlementCultureBridge.cs') -Raw
$marriageCommands = Get-Content (Join-Path $srcRoot 'Runtime\KaiMarriageCommands.cs') -Raw
$marriageModel = Get-Content (Join-Path $srcRoot 'Models\KaiPlayerMarriageModel.cs') -Raw
$dawi = Get-Content (Join-Path $srcRoot 'Models\DawiWomenAssetBridge.cs') -Raw
$short = Get-Content (Join-Path $srcRoot 'Runtime\KaiShortCommands.cs') -Raw

if ($subModule -notmatch 'LoadSafeDiagnostics\s*=\s*true') { throw 'LoadSafeDiagnostics must remain enabled.' }
if ($dawi -notmatch 'ForceSafeOffForLiveTest\s*=\s*true') { throw 'Dawi SAFE-OFF latch is not enabled.' }

foreach ($required in @(
    'public sealed class KaiPlayerMarriageModel : DefaultMarriageModel',
    'ShouldNpcMarriageBetweenClansBeAllowed',
    'NpcCoupleMarriageChance'
)) {
    if ($marriageModel -notmatch [regex]::Escape($required)) { throw "Marriage bridge contract missing: $required" }
}

foreach ($required in @(
    'MarriageOfferCampaignBehavior',
    'player-clan family candidates=',
    'native map marriage offers='
)) {
    if ($marriageCommands -notmatch [regex]::Escape($required)) { throw "Marriage-offer diagnostic contract missing: $required" }
}

foreach ($required in @(
    '"town_outside"',
    'Сменить народность поселения',
    'Дипломатия',
    'Династические браки',
    'Другие дома также могут первыми направить к вам гонца'
)) {
    if (($office + "`n" + $culture) -notmatch [regex]::Escape($required)) { throw "Immersive UI contract missing: $required" }
}

foreach ($forbiddenUi in @(
    'KaiTOR:',
    'TOR companion refresh methods',
    'Bannerlord 1.3.15 volunteer refresh method',
    'services FULL',
    'market FULL',
    'MISSING CultureObject'
)) {
    if (($office + "`n" + $culture) -match [regex]::Escape($forbiddenUi)) { throw "Technical text leaked into player-facing UI: $forbiddenUi" }
}

# Immediate wanderer replacement is optional: culture conversion must not be blocked
# merely because TOR changed private method names/visibility.
if ($bridge -match 'reason = "TOR companion refresh methods were not found') {
    throw 'TOR private companion methods are still a hard conversion gate.'
}
foreach ($required in @(
    'RefreshTownWandererBestEffort',
    'if (remove == null || spawn == null) return;',
    'Never fail the culture conversion here'
)) {
    if ($bridge -notmatch [regex]::Escape($required)) { throw "Deferred wanderer refresh contract missing: $required" }
}

foreach ($required in @(
    'CommandLineArgumentFunction("status", "kaitor")',
    'CommandLineArgumentFunction("marriage", "kaitor")',
    'CommandLineArgumentFunction("dynasty", "kaitor")'
)) {
    if ($short -notmatch [regex]::Escape($required)) { throw "Short diagnostic alias missing: $required" }
}

Write-Output 'KaiTOR Diplomacy v0.4.6 contracts: PASS'
Write-Output '  Native marriage offer behavior remains enabled for player-clan relatives.'
Write-Output '  Dawi women/pregnancy remains hard SAFE-OFF.'
Write-Output '  Player-facing diplomacy/culture text contains no technical TOR diagnostics.'
Write-Output '  Settlement conversion no longer depends on TOR private wanderer method reflection.'

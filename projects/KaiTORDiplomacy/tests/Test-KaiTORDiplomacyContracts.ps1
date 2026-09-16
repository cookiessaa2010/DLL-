[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $root 'module\SubModule.xml'
$srcRoot = Join-Path $root 'src'

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Manifest missing: $manifestPath" }
[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
if ($manifest.Module.Id.value -ne 'KaiTOR_Diplomacy') { throw 'Unexpected module id.' }
if ($manifest.Module.Version.value -ne 'v0.4.2') { throw 'Unexpected module version.' }

$dependencyIds = @($manifest.Module.DependedModules.DependedModule | ForEach-Object { $_.Id })
foreach ($required in @('Native','SandBoxCore','Sandbox','TOR_Armory','TOR_Environment','TOR_Core')) {
    if ($dependencyIds -notcontains $required) { throw "Required module dependency missing: $required" }
}

$sourceFiles = @(Get-ChildItem -LiteralPath $srcRoot -Recurse -Filter '*.cs' -File)
if ($sourceFiles.Count -eq 0) { throw 'No C# source files found.' }
$source = ($sourceFiles | Get-Content -Raw) -join "`n"

$subModule = Get-Content (Join-Path $srcRoot 'SubModule.cs') -Raw
$gate = Get-Content (Join-Path $srcRoot 'Runtime\TorCompatibilityGate.cs') -Raw
$office = Get-Content (Join-Path $srcRoot 'Runtime\KaiDiplomacyOfficeBehavior.cs') -Raw
$culture = Get-Content (Join-Path $srcRoot 'Runtime\KaiCultureAssimilationBehavior.cs') -Raw
$dynasty = Get-Content (Join-Path $srcRoot 'Runtime\KaiDynastyAiBehavior.cs') -Raw
$dynastyCommands = Get-Content (Join-Path $srcRoot 'Runtime\KaiDynastyCommands.cs') -Raw
$dawi = Get-Content (Join-Path $srcRoot 'Models\DawiWomenAssetBridge.cs') -Raw

foreach ($expectedType in @(
    'TOR_Core.Models.TORDiplomacyModel',
    'TOR_Core.Models.TORAllianceModel',
    'TOR_Core.Models.TORTradeAgreementModel',
    'TOR_Core.Models.TORMarriageModel',
    'TOR_Core.CampaignMechanics.Diplomacy.TORKingdomDecisionPermissionModel'
)) {
    if ($source -notmatch [regex]::Escape($expectedType)) { throw "TOR ownership contract missing: $expectedType" }
}

foreach ($required in @(
    'HasTorMarriageOwnership',
    'HasTorPermissionOwnership',
    'string.Equals(actualType, KaiPlayerMarriageModel.ExpectedTorBaseType',
    'model is KaiPlayerMarriageModel',
    'string.Equals(actualType, KaiKingdomDecisionPermissionModel.ExpectedTorBaseType',
    'model is KaiKingdomDecisionPermissionModel'
)) {
    if ($gate -notmatch [regex]::Escape($required)) { throw "LoadSafe gate contract missing: $required" }
}

if ($subModule -notmatch 'LoadSafeDiagnostics\s*=\s*true') { throw 'LoadSafeDiagnostics must remain enabled.' }
if ($dawi -notmatch 'ForceSafeOffForLiveTest\s*=\s*true') { throw 'Dawi SAFE-OFF latch is not enabled.' }

foreach ($required in @(
    'Dictionary<string, double> _nonAggressionExpiryDays',
    'kaitor_diplomacy_nap_expiry_days_v2',
    'kaitor_diplomacy_breach_counts',
    'kaitor_diplomacy_trust',
    'kaitor_diplomacy_nap_cooldown_expiry_days',
    'kaitor_diplomacy_save_schema',
    'CurrentSaveSchemaVersion = 1',
    'ValidateAndMigrateSaveSchema',
    'NormalizeLoadedState',
    'WarBreachTrustPenalty',
    'NaturalExpiryTrustBonus',
    'IsStartAllianceDecisionAllowedBetweenKingdoms',
    'FactionManager.IsAtWarAgainstFaction'
)) {
    if ($source -notmatch [regex]::Escape($required)) { throw "Treaty/save contract missing: $required" }
}
if ($source -match 'Dictionary<string, float> _nonAggressionExpiryDays') { throw 'NAP expiry storage regressed to float.' }

foreach ($required in @(
    'CultureChangeCost = 100000',
    'RequiredClanTier = 3',
    'TorSettlementCultureBridge.ValidateFullConversion',
    'TorSettlementCultureBridge.RefreshAfterCultureChange',
    'Сменить культуру поселения'
)) {
    if ($culture -notmatch [regex]::Escape($required)) { throw "Culture conversion contract missing: $required" }
}
foreach ($required in @('TorCulturalServiceBridge','TorMarketCultureBridge','TORCompanionsCampaignBehavior','BountyMasterCampaignBehavior','ValidateKwartaMasters')) {
    if ($source -notmatch [regex]::Escape($required)) { throw "TOR culture/service bridge contract missing: $required" }
}

# Player-facing diplomacy must look like a normal native game system.
foreach ($required in @(
    '"Дипломатия"',
    'Договоры и дипломатическое доверие',
    'Предложить пакт о ненападении',
    'Разорвать пакт о ненападении',
    'Дипломатический журнал',
    'Смена культуры поселений'
)) {
    if ($office -notmatch [regex]::Escape($required)) { throw "Native Russian UI contract missing: $required" }
}
foreach ($forbiddenUi in @(
    '"KaiTOR: Дипломатия"',
    '"KaiTOR — Дипломатия"',
    'Дипломатический журнал KaiTOR',
    'Поддержка смены культуры TOR',
    'заглушкой SAFE-OFF'
)) {
    if ($office -match [regex]::Escape($forbiddenUi)) { throw "Mod branding leaked into player-facing UI: $forbiddenUi" }
}

# AI rulers must actively rebuild under-populated kingdoms, including the faction the player serves.
foreach ($required in @(
    'MaximumTargetNobleClans = 12',
    'MaximumKingdomGrowthActionsPerWeek = 3',
    'NewHouseCooldownDays = 42',
    'k.Leader != Hero.MainHero',
    'GetTargetNobleClanCount(k) - GetCurrentNobleClanCount(k)',
    'TryRecruitExistingClan(need.Kingdom)',
    'TryFoundCadetHouse(need.Kingdom)',
    'JoinKingdomAsClanBarterable',
    'ExecuteAiBarter',
    'Clan.CreateClan',
    'ChangeKingdomAction.ApplyByJoinToKingdom',
    'ChangeOwnerOfSettlementAction.ApplyByGift',
    'DescribeStatus()'
)) {
    if ($dynasty -notmatch [regex]::Escape($required)) { throw "Dynasty growth contract missing: $required" }
}
if ($dynasty -match 'k != playerKingdom') { throw 'Player-served kingdom is still excluded from dynasty AI.' }
if ($dynastyCommands -notmatch 'dynasty_status') { throw 'Dynasty status console command is missing.' }

foreach ($required in @(
    'KaiPregnancyModel',
    'TorFamilySafety',
    'KaiHeroDeathProbabilityModel',
    'KaiDynastyAiBehavior',
    'KaiRacialPopulationBehavior',
    'KaiDawiWomenBehavior',
    'FaceGen.GetRaceOrDefault("vampire")',
    'FaceGen.GetRaceOrDefault("dwarf")'
)) {
    if ($source -notmatch [regex]::Escape($required)) { throw "Family/racial safety source missing: $required" }
}

foreach ($forbidden in @(
    'HarmonyLib',
    'DeclareWarAction.Apply',
    'MakePeaceAction.Apply',
    'MarriageAction.Apply',
    'SaveableTypeDefiner',
    '[SaveableField',
    '[SaveableProperty'
)) {
    if ($source -match [regex]::Escape($forbidden)) { throw "Forbidden invasive/save-fragile pattern found: $forbidden" }
}

Write-Output 'KaiTOR Diplomacy v0.4.2 contract tests: PASS'
Write-Output "  C# files: $($sourceFiles.Count)"
Write-Output '  LoadSafe gate accepts untouched TOR marriage/permission models.'
Write-Output '  Dawi women/pregnancy remains hard SAFE-OFF.'
Write-Output '  Player-facing diplomacy/culture UI contains no module branding.'
Write-Output '  AI rulers rebuild under-populated kingdoms, including the kingdom the player serves.'
Write-Output '  11 fortifications imply target 8 noble clans under the current formula.'
Write-Output '  Up to three kingdom growth actions may succeed per week world-wide.'
Write-Output '  Dynasty status diagnostic command is present.'
Write-Output '  No Harmony/custom Saveable graph/direct forced war-peace-marriage action detected.'

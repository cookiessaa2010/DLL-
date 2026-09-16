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
if ($manifest.Module.Version.value -ne 'v0.4.1') { throw 'Unexpected module version.' }

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
$dawi = Get-Content (Join-Path $srcRoot 'Models\DawiWomenAssetBridge.cs') -Raw

# TOR remains authoritative for the three core diplomacy model slots.
foreach ($expectedType in @(
    'TOR_Core.Models.TORDiplomacyModel',
    'TOR_Core.Models.TORAllianceModel',
    'TOR_Core.Models.TORTradeAgreementModel',
    'TOR_Core.Models.TORMarriageModel',
    'TOR_Core.CampaignMechanics.Diplomacy.TORKingdomDecisionPermissionModel'
)) {
    if ($source -notmatch [regex]::Escape($expectedType)) { throw "TOR ownership contract missing: $expectedType" }
}

# v0.4.1 regression: LoadSafe must accept the untouched TOR marriage/permission models,
# while future full mode may use KaiTOR wrappers over those exact TOR models.
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

# LoadSafe remains enabled: family/lifecycle wrappers are intentionally not installed.
if ($subModule -notmatch 'LoadSafeDiagnostics\s*=\s*true') { throw 'LoadSafeDiagnostics must remain enabled.' }

# Dawi women/pregnancy stays hard-disabled for this live-test line.
if ($dawi -notmatch 'ForceSafeOffForLiveTest\s*=\s*true') { throw 'Dawi SAFE-OFF latch is not enabled.' }

# Treaty/save state remains primitive and stable.
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

# Settlement conversion must remain full TOR-aware conversion, not a culture-field-only edit.
foreach ($required in @(
    'CultureChangeCost = 100000',
    'RequiredClanTier = 3',
    'TorSettlementCultureBridge.ValidateFullConversion',
    'TorSettlementCultureBridge.RefreshAfterCultureChange'
)) {
    if ($culture -notmatch [regex]::Escape($required)) { throw "Culture conversion contract missing: $required" }
}
foreach ($required in @('TorCulturalServiceBridge','TorMarketCultureBridge','TORCompanionsCampaignBehavior','BountyMasterCampaignBehavior','ValidateKwartaMasters')) {
    if ($source -notmatch [regex]::Escape($required)) { throw "TOR culture/service bridge contract missing: $required" }
}

# Russian UI and explicit LoadSafe/Dawi status remain visible to the player.
foreach ($required in @(
    'KaiTOR: Дипломатия',
    'Предложить пакт о ненападении',
    'Разорвать пакт о ненападении',
    'Дипломатический журнал KaiTOR',
    'SAFE-OFF'
)) {
    if ($office -notmatch [regex]::Escape($required)) { throw "Russian UI contract missing: $required" }
}

# Preserve the existing family/racial safety implementation even though LoadSafe does not install model wrappers.
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

# No invasive patching or direct forced diplomacy/marriage actions.
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

Write-Output 'KaiTOR Diplomacy v0.4.1 contract tests: PASS'
Write-Output "  C# files: $($sourceFiles.Count)"
Write-Output '  LoadSafe gate accepts untouched TOR marriage/permission models.'
Write-Output '  Future KaiTOR wrappers remain accepted only over the exact TOR base models.'
Write-Output '  Dawi women/pregnancy remains hard SAFE-OFF.'
Write-Output '  Treaty save schema and full TOR culture conversion contracts preserved.'
Write-Output '  Russian diplomacy UI contract preserved.'
Write-Output '  No Harmony/custom Saveable graph/direct forced war-peace-marriage action detected.'

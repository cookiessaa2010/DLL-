[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $root 'module\SubModule.xml'
$srcRoot = Join-Path $root 'src'
$marriagePath = Join-Path $srcRoot 'Models\KaiPlayerMarriageModel.cs'
$pregnancyPath = Join-Path $srcRoot 'Models\KaiPregnancyModel.cs'
$warningPath = Join-Path $srcRoot 'Runtime\KaiMarriageWarningBehavior.cs'
$deathPath = Join-Path $srcRoot 'Models\KaiHeroDeathProbabilityModel.cs'
$dynastyPath = Join-Path $srcRoot 'Runtime\KaiDynastyAiBehavior.cs'

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Manifest missing: $manifestPath"
}

[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
if ($manifest.Module.Id.value -ne 'KaiTOR_Diplomacy') {
    throw 'Unexpected module id.'
}
if ($manifest.Module.Version.value -ne 'v0.2.0') {
    throw 'Unexpected module version.'
}

$dependencyIds = @($manifest.Module.DependedModules.DependedModule | ForEach-Object { $_.Id })
foreach ($required in @('Native', 'SandBoxCore', 'Sandbox', 'TOR_Armory', 'TOR_Environment', 'TOR_Core')) {
    if ($dependencyIds -notcontains $required) {
        throw "Required module dependency missing: $required"
    }
}

$sourceFiles = @(Get-ChildItem -LiteralPath $srcRoot -Recurse -Filter '*.cs' -File)
if ($sourceFiles.Count -eq 0) {
    throw 'No C# source files found.'
}
$source = ($sourceFiles | Get-Content -Raw) -join "`n"
$marriageSource = Get-Content -LiteralPath $marriagePath -Raw
$pregnancySource = Get-Content -LiteralPath $pregnancyPath -Raw
$warningSource = Get-Content -LiteralPath $warningPath -Raw
$deathSource = Get-Content -LiteralPath $deathPath -Raw
$dynastySource = Get-Content -LiteralPath $dynastyPath -Raw

foreach ($expectedType in @(
    'TOR_Core.Models.TORDiplomacyModel',
    'TOR_Core.Models.TORAllianceModel',
    'TOR_Core.Models.TORTradeAgreementModel',
    'TOR_Core.Models.TORMarriageModel',
    'TOR_Core.CampaignMechanics.Diplomacy.TORKingdomDecisionPermissionModel'
)) {
    if ($source -notmatch [regex]::Escape($expectedType)) {
        throw "TOR compatibility gate does not verify $expectedType"
    }
}

foreach ($requiredPattern in @(
    'IsStartAllianceDecisionAllowedBetweenKingdoms',
    'FactionManager.IsAtWarAgainstFaction',
    'Dictionary<string, double> _nonAggressionExpiryDays',
    'kaitor_diplomacy_nap_expiry_days_v2',
    'kaitor_diplomacy_breach_counts',
    'kaitor_diplomacy_trust',
    'kaitor_diplomacy_nap_cooldown_expiry_days',
    'kaitor_diplomacy_save_schema',
    'CurrentSaveSchemaVersion = 1',
    'ValidateAndMigrateSaveSchema',
    'NormalizeLoadedState',
    'DescribeSaveCompatibility',
    'save_status',
    'WarBreachTrustPenalty',
    'NaturalExpiryTrustBonus',
    'CultureChangeCost = 100000',
    'RequiredClanTier = 3',
    'TorSettlementCultureBridge',
    'TorCulturalServiceBridge',
    'TorMarketCultureBridge',
    'IsItemPreferredForTown',
    'UpdateSupplyAndDemand',
    'BasicMercenaryTroops',
    'UpdateCurrentMercenaryTroopAndCount',
    'TORCompanionsCampaignBehavior',
    'BountyMasterCampaignBehavior',
    'tor_bountymaster_empire_0',
    '_settlementToBountyMasterMap',
    'TeefBehavior',
    'tor_kwartamasta_greenskins_0',
    'ValidateKwartaMasters',
    'KaiPregnancyModel',
    'TorFamilySafety',
    'GetDailyChanceOfPregnancyForHero',
    'CanUseVanillaPregnancy',
    'TOR_Core.Extensions.HeroExtensions, TOR_Core',
    'KaiMarriageWarningBehavior',
    'hero_courtship_final_barter',
    'BeforeHeroesMarried',
    'will not be able to have biological children',
    'CampaignOptions.IsLifeDeathCycleDisabled = false',
    'KaiHeroDeathProbabilityModel',
    'DawiOldAgeStart = 180f',
    'DawiHardMaxAge = 420f',
    'KaiDynastyAiBehavior',
    'JoinKingdomAsClanBarterable',
    'ExecuteAiBarter',
    'Clan.CreateClan',
    'ChangeKingdomAction.ApplyByJoinToKingdom',
    'ChangeOwnerOfSettlementAction.ApplyByGift',
    'kaitor_dynasty_house_cooldown_v1'
)) {
    if ($source -notmatch [regex]::Escape($requiredPattern)) {
        throw "Required diplomacy/culture/save/family/dynasty pattern missing: $requiredPattern"
    }
}

foreach ($forbidden in @(
    'HarmonyLib',
    'AddModel(new',
    'DeclareWarAction.Apply',
    'MakePeaceAction.Apply',
    'MarriageAction.Apply',
    'SaveableTypeDefiner',
    '[SaveableField',
    '[SaveableProperty'
)) {
    if ($source -match [regex]::Escape($forbidden)) {
        throw "Forbidden invasive/save-fragile pattern found: $forbidden"
    }
}

# Marriage and reproduction are deliberately separate. A direct race-equality veto in
# the marriage model would regress Dawi <-> human/elf marriages requested by design.
if ($marriageSource -match 'CharacterObject\.Race\s*!=') {
    throw 'Marriage model regressed to a direct race-equality veto; cross-race social marriage must remain possible.'
}

# TOR disables NPC marriage entirely. KaiTOR must restore a nonzero native chance and
# let Bannerlord RomanceCampaignBehavior perform MarriageAction itself.
if ($marriageSource -match 'NpcCoupleMarriageChance\(Hero firstHero, Hero secondHero\)\s*=>\s*0f') {
    throw 'World dynastic marriage regressed to disabled NPC marriage.'
}
if ($marriageSource -notmatch 'base\.NpcCoupleMarriageChance') {
    throw 'NPC marriage chance no longer delegates to Bannerlord native marriage probability.'
}
if ($marriageSource -notmatch 'ChildlessNpcMarriageChanceMultiplier') {
    throw 'Childless inter-species AI marriage rarity control is missing.'
}

# Pregnancy must fail closed before Bannerlord HeroCreator.DeliverOffSpring sees any
# cross-race world marriage, not just marriages involving the player clan.
if ($pregnancySource -notmatch 'TorFamilySafety\.CanUseVanillaPregnancy') {
    throw 'Pregnancy model no longer delegates offspring race safety to TorFamilySafety.'
}
if ($pregnancySource -match 'InvolvesPlayerClan') {
    throw 'Pregnancy safety regressed to player-only; restored NPC marriages require world-wide offspring safety.'
}

# Restoring life/death without a race-aware mortality wrapper would make long-lived TOR
# races inherit Bannerlord human old-age limits.
if ($deathSource -notmatch 'HeroDeathProbabilityCalculationModel') {
    throw 'Race-aware natural death model is missing.'
}
foreach ($lifePattern in @('"sturgia"', '"battania"', '"eonir"', '"aserai"', 'IsVampire', 'IsUndead')) {
    if ($deathSource -notmatch [regex]::Escape($lifePattern)) {
        throw "Lifecycle race rule missing: $lifePattern"
    }
}

# Dynasty growth must prefer native political barter and must be bounded.
foreach ($dynastyPattern in @(
    'MaximumTargetNobleClans = 10',
    'NewHouseCooldownDays = 180',
    'spareFiefs.Length < 2',
    'TryRecruitExistingClan',
    'TryFoundCadetHouse',
    'IsAiCompanion'
)) {
    if ($dynastySource -notmatch [regex]::Escape($dynastyPattern)) {
        throw "AI dynasty growth safety contract missing: $dynastyPattern"
    }
}

# A childless player marriage must warn before the normal final barter and give a real
# chance to back out. Warning state must remain non-persistent.
foreach ($warningPattern in @(
    'kaitor_childless_marriage_warning_options',
    'Continue with the marriage arrangements',
    'reconsider this marriage',
    'public override void SyncData(IDataStore dataStore)',
    'Intentionally empty'
)) {
    if ($warningSource -notmatch [regex]::Escape($warningPattern)) {
        throw "Childless marriage warning contract missing: $warningPattern"
    }
}

# CampaignTime.Now.ToDays is double in Bannerlord 1.3.15.
if ($source -match 'Dictionary<string, float> _nonAggressionExpiryDays') {
    throw 'NAP expiry storage regressed to float; Bannerlord 1.3.15 CampaignTime.ToDays is double.'
}

Write-Output 'KaiTOR Diplomacy contract tests: PASS'
Write-Output "  C# files: $($sourceFiles.Count)"
Write-Output '  TOR diplomacy ownership and compatibility gates preserved.'
Write-Output '  Treaty/save primitive-state contracts present.'
Write-Output '  Full settlement culture conversion and TOR cultural service hooks present.'
Write-Output '  World NPC marriages restored through Bannerlord native RomanceCampaignBehavior.'
Write-Output '  Cross-race/undead pregnancy safety applies to the whole world.'
Write-Output '  TOR frozen lifecycle is re-enabled with race-aware natural mortality.'
Write-Output '  AI kingdoms can recruit existing clans through native barter or found bounded cadet houses.'
Write-Output '  Childless player marriages retain pre-barter continue/cancel warning.'
Write-Output '  No Harmony/custom Saveable graph/direct forced war-peace-marriage action detected.'

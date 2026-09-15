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
$racialPath = Join-Path $srcRoot 'Runtime\KaiRacialPopulationBehavior.cs'
$dawiAssetPath = Join-Path $srcRoot 'Models\DawiWomenAssetBridge.cs'
$dawiPopulationPath = Join-Path $srcRoot 'Runtime\KaiDawiWomenBehavior.cs'

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Manifest missing: $manifestPath"
}

[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
if ($manifest.Module.Id.value -ne 'KaiTOR_Diplomacy') {
    throw 'Unexpected module id.'
}
if ($manifest.Module.Version.value -ne 'v0.3.0') {
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
$racialSource = Get-Content -LiteralPath $racialPath -Raw
$dawiAssetSource = Get-Content -LiteralPath $dawiAssetPath -Raw
$dawiPopulationSource = Get-Content -LiteralPath $dawiPopulationPath -Raw

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
    'kaitor_dynasty_house_cooldown_v1',
    'KaiRacialPopulationBehavior',
    'kaitor_racial_spore_pressure_v1',
    'kaitor_racial_spore_cooldown_v1',
    'kaitor_racial_bloodkiss_cooldown_v1',
    'tor_wanderer_greenskins_0',
    'HeroCreator.CreateSpecialHero',
    'TryApplyBloodKiss',
    'FaceGen.GetRaceOrDefault("vampire")',
    'DawiWomenAssetBridge',
    'kaitor_dawi_woman_lord',
    'KaiDawiWomenBehavior',
    'kaitor_dawi_women_generation_cooldown_v1'
)) {
    if ($source -notmatch [regex]::Escape($requiredPattern)) {
        throw "Required diplomacy/culture/save/family/dynasty/racial pattern missing: $requiredPattern"
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

# Dawi pregnancy may only unlock when the actual female dwarf asset chain registers its
# sentinel template. A culture check alone is not enough to prove render safety.
foreach ($dawiPattern in @(
    'FemaleDawiLordTemplateId = "kaitor_dawi_woman_lord"',
    'MBObjectManager.Instance',
    'template.IsFemale',
    'FaceGen.GetRaceOrDefault("dwarf")'
)) {
    if ($dawiAssetSource -notmatch [regex]::Escape($dawiPattern)) {
        throw "Dawi female asset safety gate missing: $dawiPattern"
    }
}

# Female Dawi population bootstrap must remain bounded, asset-gated and AI-only.
foreach ($dawiPopulationPattern in @(
    'DawiWomenAssetBridge.IsAvailable',
    'DawiFemaleMinimumAge = 30',
    'MaximumGeneratedWomenPerClan = 3',
    'GenerationCooldownDays = 336',
    'clan == Clan.PlayerClan',
    'template.Race != dwarfRace',
    'KillCharacterAction.ApplyByRemove(hero)'
)) {
    if ($dawiPopulationSource -notmatch [regex]::Escape($dawiPopulationPattern)) {
        throw "Dawi women population safety contract missing: $dawiPopulationPattern"
    }
}
if ($source -notmatch 'new KaiDawiWomenBehavior\(\)') {
    throw 'Dawi women behavior is not registered in the campaign module.'
}

# Greenskins must grow through off-screen spore population, never Bannerlord pregnancy.
foreach ($sporePattern in @(
    'SporePressureThreshold = 100d',
    'SporeSpawnCooldownDays = 84',
    'TrySpawnSporeBornHero',
    'TorFamilySafety.TryAddAttribute(hero, "AICompanion")',
    'KillCharacterAction.ApplyByRemove(hero)'
)) {
    if ($racialSource -notmatch [regex]::Escape($sporePattern)) {
        throw "Greenskin spore lifecycle contract missing: $sporePattern"
    }
}
if ($racialSource -match 'DeliverOffSpring|GetDailyChanceOfPregnancyForHero') {
    throw 'Racial population behavior must not route Greenskins/vampires through Bannerlord pregnancy.'
}

# Blood Kiss must mutate an existing mortal hero's race and must not manufacture a
# biological vampire child or force a career/religion change owned by TOR.
foreach ($bloodPattern in @(
    'BloodKissCooldownDays = 180',
    'MaximumVampireScions = 8',
    'IsEligibleForBloodKiss',
    'TorFamilySafety.TryApplyBloodKiss(candidate)'
)) {
    if ($racialSource -notmatch [regex]::Escape($bloodPattern)) {
        throw "Vampire Blood Kiss contract missing: $bloodPattern"
    }
}
if ($racialSource -match 'AddCareer|DominantReligion|AddReligiousInfluence') {
    throw 'KaiTOR racial population behavior must not fabricate TOR vampire careers or religion.'
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
Write-Output '  Dawi pregnancy remains gated behind the real female-dwarf asset sentinel.'
Write-Output '  Female Dawi AI population bootstrap is bounded, asset-gated and player-clan safe.'
Write-Output '  Greenskin population continuity uses bounded off-screen spore-born adult heroes.'
Write-Output '  Vampire population continuity uses bounded Blood Kiss race conversion.'
Write-Output '  TOR frozen lifecycle is re-enabled with race-aware natural mortality.'
Write-Output '  AI kingdoms can recruit existing clans through native barter or found bounded cadet houses.'
Write-Output '  Childless player marriages retain pre-barter continue/cancel warning.'
Write-Output '  No Harmony/custom Saveable graph/direct forced war-peace-marriage action detected.'

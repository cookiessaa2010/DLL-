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
    'will not be able to have biological children'
)) {
    if ($source -notmatch [regex]::Escape($requiredPattern)) {
        throw "Required diplomacy/culture/save/family safety pattern missing: $requiredPattern"
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

# Pregnancy must fail closed before Bannerlord HeroCreator.DeliverOffSpring sees a
# cross-race player marriage.
if ($pregnancySource -notmatch 'TorFamilySafety\.CanUseVanillaPregnancy') {
    throw 'Pregnancy model no longer delegates offspring race safety to TorFamilySafety.'
}

# A childless marriage must warn the player before the normal final barter and give a
# real chance to back out. Warning state must remain non-persistent.
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

# CampaignTime.Now.ToDays is double in Bannerlord 1.3.15. A float expiry map would
# either fail compilation or require lossy casts, so make that regression explicit.
if ($source -match 'Dictionary<string, float> _nonAggressionExpiryDays') {
    throw 'NAP expiry storage regressed to float; Bannerlord 1.3.15 CampaignTime.ToDays is double.'
}

Write-Output 'KaiTOR Diplomacy contract tests: PASS'
Write-Output "  C# files: $($sourceFiles.Count)"
Write-Output '  TOR model ownership preserved.'
Write-Output '  Treaty time storage matches Bannerlord 1.3.15 CampaignTime precision.'
Write-Output '  Trust/breach/cooldown state contract present.'
Write-Output '  Save schema migration and stable primitive SyncData contract present.'
Write-Output '  No custom SaveableTypeDefiner/SaveableField/SaveableProperty dependencies detected.'
Write-Output '  Full culture conversion contract present: tier 3+, 100,000 denars.'
Write-Output '  Recruitment, companions, cultural services and culture-aware market hooks present.'
Write-Output '  Empire Bounty Master and Greenskin Kwartamasta refresh contracts present.'
Write-Output '  Player marriage is decoupled from TOR offspring race safety.'
Write-Output '  Childless player marriages show a pre-barter warning with continue/cancel choices.'
Write-Output '  Cross-race pregnancy guard is installed without altering TOR NPC families.'
Write-Output '  No Harmony/model replacement/forced war-peace-marriage actions detected.'

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $root 'module\SubModule.xml'
$projectPath = Join-Path $root 'src\KaiTOR_Diplomacy.csproj'
$srcRoot = Join-Path $root 'src'

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Manifest missing: $manifestPath" }
[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
if ($manifest.Module.Id.value -ne 'KaiTOR_Diplomacy') { throw 'Unexpected module id.' }
if ($manifest.Module.Version.value -ne 'v0.4.5') { throw 'Unexpected module version.' }

$project = Get-Content -LiteralPath $projectPath -Raw
foreach ($required in @(
    '<Version>0.4.5</Version>',
    '<AssemblyVersion>0.4.5.0</AssemblyVersion>',
    '<FileVersion>0.4.5.0</FileVersion>',
    '<InformationalVersion>0.4.5-NativeMarriage-CadetQueue-TownNationality</InformationalVersion>'
)) {
    if ($project -notmatch [regex]::Escape($required)) { throw "Assembly version stamp missing: $required" }
}

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
$cadets = Get-Content (Join-Path $srcRoot 'Runtime\KaiCadetHouseSafeBehavior.cs') -Raw
$dynastyCommands = Get-Content (Join-Path $srcRoot 'Runtime\KaiDynastyCommands.cs') -Raw
$marriageCommands = Get-Content (Join-Path $srcRoot 'Runtime\KaiMarriageCommands.cs') -Raw
$marriageModel = Get-Content (Join-Path $srcRoot 'Models\KaiPlayerMarriageModel.cs') -Raw
$racial = Get-Content (Join-Path $srcRoot 'Runtime\KaiRacialPopulationBehavior.cs') -Raw
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
    'model is KaiPlayerMarriageModel'
)) {
    if ($gate -notmatch [regex]::Escape($required)) { throw "Compatibility gate contract missing: $required" }
}

if ($subModule -notmatch 'LoadSafeDiagnostics\s*=\s*true') { throw 'LoadSafeDiagnostics must remain enabled.' }
if ($dawi -notmatch 'ForceSafeOffForLiveTest\s*=\s*true') { throw 'Dawi SAFE-OFF latch is not enabled.' }

# v0.4.5 selectively restores only MarriageModel in LoadSafe. Pregnancy/death/lifecycle
# wrappers and custom courtship graph hooks remain behind the full-mode gate.
$marriageInstall = 'InstallMarriageWrapper\(campaignStarter\);'
if ([regex]::Matches($subModule, $marriageInstall).Count -ne 1) { throw 'Marriage wrapper must be installed exactly once.' }
$ifIndex = $subModule.IndexOf('if (!LoadSafeDiagnostics)', [System.StringComparison]::Ordinal)
$marriageIndex = $subModule.IndexOf('InstallMarriageWrapper(campaignStarter);', [System.StringComparison]::Ordinal)
if ($marriageIndex -lt 0 -or $ifIndex -lt 0 -or $marriageIndex -gt $ifIndex) {
    throw 'Marriage wrapper is not installed in the LoadSafe path.'
}

$nonLoadSafeBlock = [regex]::Match(
    $subModule,
    'if \(!LoadSafeDiagnostics\)\s*\{(?<body>[\s\S]*?)\n\s*\}\s*\n\s*// LoadSafe runtime:')
if (-not $nonLoadSafeBlock.Success) { throw 'Could not verify non-LoadSafe isolation block.' }
$nonLoadSafeBody = $nonLoadSafeBlock.Groups['body'].Value
foreach ($required in @(
    'InstallPregnancyWrapper(campaignStarter);',
    'InstallHeroDeathWrapper(campaignStarter);',
    'KaiMarriageWarningBehavior',
    'KaiRacialPopulationBehavior',
    'KaiDawiWomenBehavior'
)) {
    if ($nonLoadSafeBody -notmatch [regex]::Escape($required)) { throw "Unsafe family mutation escaped isolation contract: $required" }
}

foreach ($behavior in @(
    'KaiDiplomacyBehavior',
    'KaiDiplomacyOfficeBehavior',
    'KaiDiplomacyAiBehavior',
    'KaiCultureAssimilationBehavior',
    'KaiDynastyAiBehavior',
    'KaiCadetHouseSafeBehavior'
)) {
    $registration = "campaignStarter\.AddBehavior\(new $behavior\(\)\);"
    if ([regex]::Matches($subModule, $registration).Count -ne 1) { throw "Expected exactly one $behavior registration." }
    if ($nonLoadSafeBody -match $registration) { throw "$behavior was accidentally moved behind the full-mode gate." }
}

# Native marriage bridge: do not reproduce Bannerlord's romance/offer graph ourselves.
foreach ($required in @(
    'public sealed class KaiPlayerMarriageModel : DefaultMarriageModel',
    'IsCoupleSuitableForMarriage',
    'ShouldNpcMarriageBetweenClansBeAllowed',
    'NpcCoupleMarriageChance'
)) {
    if ($marriageModel -notmatch [regex]::Escape($required)) { throw "Marriage bridge contract missing: $required" }
}
foreach ($required in @('marriage_status','RomanceCampaignBehavior','MarriageOfferCampaignBehavior')) {
    if ($marriageCommands -notmatch [regex]::Escape($required)) { throw "Marriage diagnostic contract missing: $required" }
}

foreach ($required in @(
    'Dictionary<string, double> _nonAggressionExpiryDays',
    'kaitor_diplomacy_nap_expiry_days_v2',
    'kaitor_diplomacy_trust',
    'WarBreachTrustPenalty',
    'NaturalExpiryTrustBonus',
    'IsStartAllianceDecisionAllowedBetweenKingdoms',
    'FactionManager.IsAtWarAgainstFaction'
)) {
    if ($source -notmatch [regex]::Escape($required)) { throw "Treaty/save contract missing: $required" }
}

# TOR live town screen is town_outside. The nationality action must be reachable there.
foreach ($required in @(
    'CultureChangeCost = 100000',
    'RequiredClanTier = 3',
    '"town_outside"',
    'Сменить народность поселения',
    'TorSettlementCultureBridge.ValidateFullConversion',
    'TorSettlementCultureBridge.RefreshAfterCultureChange'
)) {
    if ($culture -notmatch [regex]::Escape($required)) { throw "Settlement nationality contract missing: $required" }
}
foreach ($required in @('"town_outside"','"Дипломатия"','Брачные союзы','Смена народности поселений')) {
    if ($office -notmatch [regex]::Escape($required)) { throw "Town diplomacy UI contract missing: $required" }
}
foreach ($forbiddenUi in @('"KaiTOR: Дипломатия"','"KaiTOR — Дипломатия"','Дипломатический журнал KaiTOR')) {
    if ($office -match [regex]::Escape($forbiddenUi)) { throw "Mod branding leaked into player-facing UI: $forbiddenUi" }
}

# Existing-clan recruitment stays conservative on weekly tick.
foreach ($required in @(
    'MaximumKingdomGrowthActionsPerWeek = 1',
    'EnableCadetHouseCreation = false',
    'TryRecruitExistingClan(need.Kingdom)',
    'JoinKingdomAsClanBarterable',
    'ExecuteAiBarter'
)) {
    if ($dynasty -notmatch [regex]::Escape($required)) { throw "Recruitment safety contract missing: $required" }
}

# Cadet houses are preserved as a feature but staged away from the world tick.
foreach ($required in @(
    'MinimumClanDeficitForCadetHouse = 2',
    'PerKingdomCooldownDays = 84',
    'GlobalCooldownDays = 42',
    'PendingDelayDays = 1',
    'CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick)',
    'CampaignEvents.AfterSettlementEntered.AddNonSerializedListener(this, OnAfterSettlementEntered)',
    'TryQueueCadetHouse',
    'party != MobileParty.MainParty',
    'hero.PartyBelongedTo != null',
    'hero.GovernorOf != null',
    'Clan.CreateClan',
    'ChangeKingdomAction.ApplyByJoinToKingdom',
    'ChangeOwnerOfSettlementAction.ApplyByGift',
    'CampaignEventDispatcher.Instance.OnClanCreated(newClan, true)'
)) {
    if ($cadets -notmatch [regex]::Escape($required)) { throw "Staged cadet-house contract missing: $required" }
}
$queueIndex = $cadets.IndexOf('TryQueueCadetHouse(need.Kingdom, now)', [System.StringComparison]::Ordinal)
$commitIndex = $cadets.IndexOf('OnAfterSettlementEntered', [System.StringComparison]::Ordinal)
$createIndex = $cadets.IndexOf('var newClan = Clan.CreateClan', [System.StringComparison]::Ordinal)
if ($queueIndex -lt 0 -or $commitIndex -lt 0 -or $createIndex -lt 0 -or $createIndex -lt $commitIndex) {
    throw 'Cadet clan creation is not deferred behind settlement entry.'
}
if ($dynastyCommands -notmatch 'KaiCadetHouseSafeBehavior') { throw 'dynasty_status does not report the staged cadet queue.' }

foreach ($required in @('HeroCreator.CreateSpecialHero','TryApplyBloodKiss','GreenskinWandererTemplateIds')) {
    if ($racial -notmatch [regex]::Escape($required)) { throw "Full-mode racial source unexpectedly missing: $required" }
}

foreach ($forbidden in @(
    'HarmonyLib',
    'DeclareWarAction.Apply',
    'MakePeaceAction.Apply',
    'SaveableTypeDefiner',
    '[SaveableField',
    '[SaveableProperty'
)) {
    if ($source -match [regex]::Escape($forbidden)) { throw "Forbidden invasive/save-fragile pattern found: $forbidden" }
}

Write-Output 'KaiTOR Diplomacy v0.4.5 contract tests: PASS'
Write-Output "  C# files: $($sourceFiles.Count)"
Write-Output '  Native Bannerlord marriage model/offer flow is restored without custom LoadSafe courtship injection.'
Write-Output '  Dawi women/pregnancy remains hard SAFE-OFF.'
Write-Output '  TOR town_outside exposes Diplomacy and Change settlement nationality.'
Write-Output '  AI recruitment stays native-barter only on WeeklyTick.'
Write-Output '  Cadet houses use existing ruling-clan lords and commit only on safe settlement entry.'
Write-Output '  Cadet creation: deficit >=2, one pending globally, 42d global and 84d per-kingdom cooldown.'

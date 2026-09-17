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
if ($manifest.Module.Version.value -ne 'v0.5.2') { throw 'Unexpected module version.' }

$project = Get-Content -LiteralPath $projectPath -Raw
foreach ($required in @(
    '<Version>0.5.2</Version>',
    '<AssemblyVersion>0.5.2.0</AssemblyVersion>',
    '<FileVersion>0.5.2.0</FileVersion>',
    '<InformationalVersion>0.5.2-StagedRealmGrowth</InformationalVersion>'
)) {
    if ($project -notmatch [regex]::Escape($required)) { throw "Assembly version stamp missing: $required" }
}

$subModule = Get-Content (Join-Path $srcRoot 'SubModule.cs') -Raw
$cadet = Get-Content (Join-Path $srcRoot 'Runtime\KaiCadetHouseSafeBehavior.cs') -Raw
$family = Get-Content (Join-Path $srcRoot 'Runtime\KaiFamilyAffairsBehavior.cs') -Raw
$marriage = Get-Content (Join-Path $srcRoot 'Models\KaiPlayerMarriageModel.cs') -Raw
$familySafety = Get-Content (Join-Path $srcRoot 'Models\TorFamilySafety.cs') -Raw
$dynasty = Get-Content (Join-Path $srcRoot 'Runtime\KaiDynastyAiBehavior.cs') -Raw
$mercy = Get-Content (Join-Path $srcRoot 'Runtime\KaiMercyRelationBehavior.cs') -Raw
$culture = Get-Content (Join-Path $srcRoot 'Runtime\KaiCultureAssimilationBehavior.cs') -Raw
$office = Get-Content (Join-Path $srcRoot 'Runtime\KaiDiplomacyOfficeBehavior.cs') -Raw
$dawi = Get-Content (Join-Path $srcRoot 'Models\DawiWomenAssetBridge.cs') -Raw

if ($subModule -notmatch 'LoadSafeDiagnostics\s*=\s*true') { throw 'LoadSafeDiagnostics must remain enabled.' }
if ($dawi -notmatch 'ForceSafeOffForLiveTest\s*=\s*true') { throw 'Dawi SAFE-OFF latch is not enabled.' }

foreach ($required in @(
    'campaignStarter.AddBehavior(new KaiFamilyAffairsBehavior());',
    'campaignStarter.AddBehavior(new KaiDynastyAiBehavior());',
    'campaignStarter.AddBehavior(new KaiCadetHouseSafeBehavior());',
    'campaignStarter.AddBehavior(new KaiMercyRelationBehavior());'
)) {
    if ($subModule -notmatch [regex]::Escape($required)) { throw "LoadSafe behavior registration missing: $required" }
}

$cadetRegistration = 'campaignStarter.AddBehavior(new KaiCadetHouseSafeBehavior());'
if (($subModule.Split($cadetRegistration).Length - 1) -ne 1) { throw 'Staged new-house behavior must be registered exactly once.' }
$unsafeBlockStart = $subModule.IndexOf('if (!LoadSafeDiagnostics)', [System.StringComparison]::Ordinal)
$loadSafeComment = $subModule.IndexOf('// LoadSafe runtime systems.', [System.StringComparison]::Ordinal)
$cadetPosition = $subModule.IndexOf($cadetRegistration, [System.StringComparison]::Ordinal)
if ($unsafeBlockStart -lt 0 -or $loadSafeComment -lt 0 -or $cadetPosition -lt $loadSafeComment) {
    throw 'Staged new-house behavior must be registered in the LoadSafe runtime block.'
}

foreach ($required in @(
    'CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);',
    'CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);',
    'MinimumClanDeficitForNewHouse = 2',
    'PerKingdomCooldownDays = 84',
    'GlobalCooldownDays = 42',
    'PendingDelayDays = 2',
    'PhaseDelayHours = 6',
    'NewHouseMinimumRulerGold = 50000',
    'mainParty.CurrentSettlement != null',
    'mainParty.MapEvent != null',
    'mainParty.IsMoving',
    'CampaignTime.Now.GetHourOfDay',
    'RemoveCompanionAction.ApplyByByTurningToLord',
    'newClan.Kingdom = kingdom;',
    'founder.Clan = newClan;',
    'newClan.SetLeader(founder);',
    'CampaignEventDispatcher.Instance.OnClanCreated(newClan, true);',
    'newHouses=landless'
)) {
    if ($cadet -notmatch [regex]::Escape($required)) { throw "Staged realm-growth contract missing: $required" }
}
if ($cadet -match 'AfterSettlementEntered') { throw 'New-house mutation must not execute on settlement entry.' }
if ($cadet -match 'ChangeOwnerOfSettlementAction\.ApplyByGift') { throw 'New houses must not receive a fief during creation.' }
if ($cadet -match 'ChangeKingdomAction\.ApplyByJoinToKingdom') { throw 'Staged path must initialize kingdom ownership in native companion-clan order.' }

foreach ($required in @(
    'Семейные дела',
    'Принять в род',
    'Удочерить',
    'Усыновить',
    'AdoptHeroAction.Apply(candidate)',
    'Супруг или супруга для усыновления не требуются'
)) {
    if ($family -notmatch [regex]::Escape($required)) { throw "Family/adoption contract missing: $required" }
}

foreach ($required in @(
    'IsPlayerClanFemaleCouple',
    'GetClanAfterMarriage',
    'IsCertifiedNpcMarriagePair',
    'TorFamilySafety.CanUseVanillaPregnancy(firstHero, secondHero)'
)) {
    if ($marriage -notmatch [regex]::Escape($required)) { throw "Marriage safety contract missing: $required" }
}
if ($familySafety -notmatch [regex]::Escape('if (firstHero.IsFemale == secondHero.IsFemale)')) { throw 'Same-sex marriages must remain outside vanilla pregnancy.' }

if ($dynasty -notmatch [regex]::Escape('JoinKingdomAsClanBarterable')) { throw 'AI rulers must retain native existing-clan recruitment.' }
if ($dynasty -notmatch [regex]::Escape('ExecuteAiBarter')) { throw 'AI ruler recruitment must use native barter.' }
if ($dynasty -notmatch [regex]::Escape('k.Leader != Hero.MainHero')) { throw 'NPC-led kingdoms including the player faction must be managed.' }
if ($dynasty -notmatch [regex]::Escape('EnableCadetHouseCreation = false')) { throw 'Legacy in-tick clan creation must remain disabled.' }

foreach ($required in @('PostBattleMercyRelationBonus = 50','EndCaptivityDetail.ReleasedAfterBattle')) {
    if ($mercy -notmatch [regex]::Escape($required)) { throw "Mercy contract regression: $required" }
}
foreach ($required in @('AddCultureMenuOption(starter, "town_outside"','Сменить народность поселения')) {
    if ($culture -notmatch [regex]::Escape($required)) { throw "Settlement nationality UI regression: $required" }
}
foreach ($required in @('AddOfficeMenuOption(starter, "town_outside"','"Дипломатия"')) {
    if ($office -notmatch [regex]::Escape($required)) { throw "Diplomacy UI regression: $required" }
}

Write-Output 'KaiTOR Diplomacy v0.5.2 contracts: PASS'
Write-Output '  New houses are restored through a two-phase hourly queue, never on settlement entry.'
Write-Output '  AI rulers still recruit existing clans through native barter before new-house growth.'
Write-Output '  New houses start landless; no fief transfer is bundled into clan creation.'
Write-Output '  Dawi women/pregnancy remains hard SAFE-OFF while Dawi/greenskin political clan growth is available.'
Write-Output '  Family/adoption, marriage safety, mercy +50, Diplomacy and nationality UI remain present.'

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
if ($manifest.Module.Version.value -ne 'v0.5.0') { throw 'Unexpected module version.' }

$project = Get-Content -LiteralPath $projectPath -Raw
foreach ($required in @(
    '<Version>0.5.0</Version>',
    '<AssemblyVersion>0.5.0.0</AssemblyVersion>',
    '<FileVersion>0.5.0.0</FileVersion>',
    '<InformationalVersion>0.5.0-FamilyAndRealmGrowth</InformationalVersion>'
)) {
    if ($project -notmatch [regex]::Escape($required)) { throw "Assembly version stamp missing: $required" }
}

$subModule = Get-Content (Join-Path $srcRoot 'SubModule.cs') -Raw
$family = Get-Content (Join-Path $srcRoot 'Runtime\KaiFamilyAffairsBehavior.cs') -Raw
$marriage = Get-Content (Join-Path $srcRoot 'Models\KaiPlayerMarriageModel.cs') -Raw
$familySafety = Get-Content (Join-Path $srcRoot 'Models\TorFamilySafety.cs') -Raw
$cadet = Get-Content (Join-Path $srcRoot 'Runtime\KaiCadetHouseSafeBehavior.cs') -Raw
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
    if ($subModule -notmatch [regex]::Escape($required)) { throw "Runtime behavior registration missing: $required" }
}

foreach ($required in @(
    'Семейные дела',
    'Принять в род',
    'Удочерить',
    'Усыновить',
    'AdoptHeroAction.Apply(candidate)',
    'RemoveCompanionAction.ApplyByByTurningToLord',
    'hero.CompanionOf != playerClan',
    'hero.Father != null || hero.Mother != null',
    'hero.CharacterObject.Race != mainHero.CharacterObject.Race',
    'MaximumLivingChildren = 6'
)) {
    if ($family -notmatch [regex]::Escape($required)) { throw "Family/adoption contract missing: $required" }
}

foreach ($required in @(
    'IsPlayerClanFemaleCouple',
    'firstHero?.IsFemale == true',
    'secondHero?.IsFemale == true',
    'firstHero.Clan == Clan.PlayerClan || secondHero.Clan == Clan.PlayerClan',
    'GetClanAfterMarriage',
    'AreHeroesRelated(firstHero, secondHero, 3)',
    'firstHero.CanMarry() && secondHero.CanMarry()'
)) {
    if ($marriage -notmatch [regex]::Escape($required)) { throw "Female marriage contract missing: $required" }
}
if ($familySafety -notmatch [regex]::Escape('if (firstHero.IsFemale == secondHero.IsFemale)')) { throw 'Same-sex marriages must remain outside vanilla pregnancy.' }

foreach ($required in @(
    'MinimumClanDeficitForNewHouse = 1',
    'PerKingdomCooldownDays = 63',
    'GlobalCooldownDays = 21',
    'CampaignEvents.WeeklyTickEvent.AddNonSerializedListener',
    'CampaignEvents.AfterSettlementEntered.AddNonSerializedListener',
    'clan == Clan.PlayerClan',
    'hero.IsLord || hero.CompanionOf == sourceClan || TorFamilySafety.IsAiCompanion(hero)',
    'RemoveCompanionAction.ApplyByByTurningToLord',
    'newClan.SetInitialHomeSettlement(homeSettlement)',
    'ChangeKingdomAction.ApplyByJoinToKingdom',
    'newHouses=landless'
)) {
    if ($cadet -notmatch [regex]::Escape($required)) { throw "Safe new-house contract missing: $required" }
}
foreach ($forbidden in @(
    'HeroCreator.Create',
    'ChangeOwnerOfSettlementAction.ApplyByGift'
)) {
    if ($cadet -match [regex]::Escape($forbidden)) { throw "Unsafe world-growth path present: $forbidden" }
}

if ($dynasty -notmatch [regex]::Escape('JoinKingdomAsClanBarterable')) { throw 'AI rulers must retain native existing-clan recruitment.' }
if ($dynasty -notmatch [regex]::Escape('ExecuteAiBarter')) { throw 'AI ruler recruitment must use native barter.' }
if ($dynasty -notmatch [regex]::Escape('k.Leader != Hero.MainHero')) { throw 'NPC-led kingdoms including the player faction must be managed.' }

foreach ($required in @(
    'PostBattleMercyRelationBonus = 50',
    'EndCaptivityDetail.ReleasedAfterBattle'
)) {
    if ($mercy -notmatch [regex]::Escape($required)) { throw "Mercy contract regression: $required" }
}

foreach ($required in @(
    'AddCultureMenuOption(starter, "town_outside"',
    'Сменить народность поселения'
)) {
    if ($culture -notmatch [regex]::Escape($required)) { throw "Settlement nationality UI regression: $required" }
}
foreach ($required in @(
    'AddOfficeMenuOption(starter, "town_outside"',
    '"Дипломатия"'
)) {
    if ($office -notmatch [regex]::Escape($required)) { throw "Diplomacy UI regression: $required" }
}

Write-Output 'KaiTOR Diplomacy v0.5.0 contracts: PASS'
Write-Output '  Family Affairs provides adoption through Bannerlord public actions.'
Write-Output '  Female/female marriage is supported for the player house and remains biologically childless.'
Write-Output '  NPC realms recruit existing clans and can found throttled landless houses from existing lords/companions.'
Write-Output '  Dawi women/pregnancy remains hard SAFE-OFF; Dawi and greenskins can still grow through political houses.'
Write-Output '  Post-battle mercy +50, Diplomacy UI and settlement nationality UI remain present.'

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
if ($manifest.Module.Version.value -ne 'v0.5.1') { throw 'Unexpected module version.' }

$project = Get-Content -LiteralPath $projectPath -Raw
foreach ($required in @(
    '<Version>0.5.1</Version>',
    '<AssemblyVersion>0.5.1.0</AssemblyVersion>',
    '<FileVersion>0.5.1.0</FileVersion>',
    '<InformationalVersion>0.5.1-FamilyUI-WorldSafetyHotfix</InformationalVersion>'
)) {
    if ($project -notmatch [regex]::Escape($required)) { throw "Assembly version stamp missing: $required" }
}

$subModule = Get-Content (Join-Path $srcRoot 'SubModule.cs') -Raw
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
    'campaignStarter.AddBehavior(new KaiMercyRelationBehavior());'
)) {
    if ($subModule -notmatch [regex]::Escape($required)) { throw "LoadSafe behavior registration missing: $required" }
}

$cadetRegistration = 'campaignStarter.AddBehavior(new KaiCadetHouseSafeBehavior());'
if (($subModule.Split($cadetRegistration).Length - 1) -ne 1) { throw 'Cadet-house behavior must be registered exactly once.' }
$unsafeBlockStart = $subModule.IndexOf('if (!LoadSafeDiagnostics)', [System.StringComparison]::Ordinal)
$cadetPosition = $subModule.IndexOf($cadetRegistration, [System.StringComparison]::Ordinal)
$loadSafeComment = $subModule.IndexOf('// LoadSafe runtime systems.', [System.StringComparison]::Ordinal)
if ($unsafeBlockStart -lt 0 -or $cadetPosition -lt $unsafeBlockStart -or $cadetPosition -gt $loadSafeComment) {
    throw 'Cadet-house runtime mutation must remain outside LoadSafe.'
}

foreach ($required in @(
    'Семейные дела',
    'Принять в род',
    'Удочерить',
    'Усыновить',
    'AdoptHeroAction.Apply(candidate)',
    'RemoveCompanionAction.ApplyByByTurningToLord',
    'hero.CompanionOf == playerClan || hero.Clan == playerClan',
    'Супруг или супруга для усыновления не требуются',
    'MaximumLivingChildren = 6'
)) {
    if ($family -notmatch [regex]::Escape($required)) { throw "Family/adoption contract missing: $required" }
}
$hideInquiryCount = ([regex]::Matches($family, [regex]::Escape('InformationManager.HideInquiry();'))).Count
if ($hideInquiryCount -lt 2) { throw 'Nested family/adoption inquiries must close the current inquiry before opening the next.' }

foreach ($required in @(
    'IsPlayerClanFemaleCouple',
    'firstHero?.IsFemale == true',
    'secondHero?.IsFemale == true',
    'GetClanAfterMarriage',
    'AreHeroesRelated(firstHero, secondHero, 3)',
    'IsCertifiedNpcMarriagePair',
    'IsCertifiedNpcDynastyCulture',
    'TorFamilySafety.CanUseVanillaPregnancy(firstHero, secondHero)',
    'string.Equals(firstHero.Culture?.StringId, secondHero.Culture?.StringId, StringComparison.Ordinal)'
)) {
    if ($marriage -notmatch [regex]::Escape($required)) { throw "Marriage safety contract missing: $required" }
}
foreach ($requiredCulture in @('"empire"','"vlandia"','"battania"','"eonir"')) {
    if ($marriage -notmatch [regex]::Escape($requiredCulture)) { throw "Certified NPC dynasty culture missing: $requiredCulture" }
}
if ($familySafety -notmatch [regex]::Escape('if (firstHero.IsFemale == secondHero.IsFemale)')) { throw 'Same-sex marriages must remain outside vanilla pregnancy.' }

if ($dynasty -notmatch [regex]::Escape('JoinKingdomAsClanBarterable')) { throw 'AI rulers must retain native existing-clan recruitment.' }
if ($dynasty -notmatch [regex]::Escape('ExecuteAiBarter')) { throw 'AI ruler recruitment must use native barter.' }
if ($dynasty -notmatch [regex]::Escape('k.Leader != Hero.MainHero')) { throw 'NPC-led kingdoms including the player faction must be managed.' }
if ($dynasty -notmatch [regex]::Escape('EnableCadetHouseCreation = false')) { throw 'Legacy in-tick cadet creation must remain disabled.' }

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

Write-Output 'KaiTOR Diplomacy v0.5.1 contracts: PASS'
Write-Output '  Family Affairs closes nested inquiries correctly and adoption always explains why it is unavailable.'
Write-Output '  Adoption never steals heroes from AI clans and uses Bannerlord public family actions.'
Write-Output '  Random NPC marriage is limited to certified same-culture living family pairs.'
Write-Output '  Runtime new-house graph mutation is suspended in LoadSafe; native AI clan recruitment remains active.'
Write-Output '  Dawi women/pregnancy remains hard SAFE-OFF.'
Write-Output '  Post-battle mercy +50, Diplomacy UI and settlement nationality UI remain present.'

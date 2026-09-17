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
if ($manifest.Module.Version.value -ne 'v0.4.7') { throw 'Unexpected module version.' }

$project = Get-Content -LiteralPath $projectPath -Raw
foreach ($required in @(
    '<Version>0.4.7</Version>',
    '<AssemblyVersion>0.4.7.0</AssemblyVersion>',
    '<FileVersion>0.4.7.0</FileVersion>',
    '<InformationalVersion>0.4.7-PostBattleMercy50</InformationalVersion>'
)) {
    if ($project -notmatch [regex]::Escape($required)) { throw "Assembly version stamp missing: $required" }
}

$subModule = Get-Content (Join-Path $srcRoot 'SubModule.cs') -Raw
$mercy = Get-Content (Join-Path $srcRoot 'Runtime\KaiMercyRelationBehavior.cs') -Raw
$dawi = Get-Content (Join-Path $srcRoot 'Models\DawiWomenAssetBridge.cs') -Raw

if ($subModule -notmatch 'LoadSafeDiagnostics\s*=\s*true') { throw 'LoadSafeDiagnostics must remain enabled.' }
if ($dawi -notmatch 'ForceSafeOffForLiveTest\s*=\s*true') { throw 'Dawi SAFE-OFF latch is not enabled.' }
if ($subModule -notmatch [regex]::Escape('campaignStarter.AddBehavior(new KaiMercyRelationBehavior());')) { throw 'Mercy behavior is not registered.' }

foreach ($required in @(
    'PostBattleMercyRelationBonus = 50',
    'CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener',
    'detail != EndCaptivityDetail.ReleasedAfterBattle',
    '!prisoner.IsLord',
    'formerCaptorParty != mainParty.Party',
    'ChangeRelationAction.ApplyPlayerRelation',
    'запомнит проявленное вами милосердие'
)) {
    if ($mercy -notmatch [regex]::Escape($required)) { throw "Post-battle mercy contract missing: $required" }
}

foreach ($forbidden in @(
    'EndCaptivityDetail.Ransom)',
    'EndCaptivityDetail.ReleasedAfterPeace)',
    'EndCaptivityDetail.ReleasedAfterEscape)',
    'EndCaptivityDetail.ReleasedByCompensation)'
)) {
    if ($mercy -match [regex]::Escape($forbidden)) { throw "Mercy bonus must not be attached to non-battle release path: $forbidden" }
}

Write-Output 'KaiTOR Diplomacy v0.4.7 contracts: PASS'
Write-Output '  Releasing a defeated lord after battle grants +50 relationship through the native release event.'
Write-Output '  Ransom, peace, escape and compensation releases are excluded.'
Write-Output '  Dawi women/pregnancy remains hard SAFE-OFF.'

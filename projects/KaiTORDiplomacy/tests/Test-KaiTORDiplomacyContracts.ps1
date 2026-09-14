[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $root 'module\SubModule.xml'
$srcRoot = Join-Path $root 'src'

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Manifest missing: $manifestPath"
}

[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
if ($manifest.Module.Id.value -ne 'KaiTOR_Diplomacy') {
    throw 'Unexpected module id.'
}
if ($manifest.Module.Version.value -ne 'v0.1.0') {
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
    'kaitor_diplomacy_trust',
    'kaitor_diplomacy_nap_cooldown_expiry_days',
    'WarBreachTrustPenalty',
    'NaturalExpiryTrustBonus'
)) {
    if ($source -notmatch [regex]::Escape($requiredPattern)) {
        throw "Required diplomacy safety/state pattern missing: $requiredPattern"
    }
}

foreach ($forbidden in @(
    'HarmonyLib',
    'AddModel(new',
    'DeclareWarAction.Apply',
    'MakePeaceAction.Apply',
    'MarriageAction.Apply'
)) {
    if ($source -match [regex]::Escape($forbidden)) {
        throw "Forbidden invasive diplomacy pattern found: $forbidden"
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
Write-Output '  No Harmony/model replacement/forced war-peace-marriage actions detected.'

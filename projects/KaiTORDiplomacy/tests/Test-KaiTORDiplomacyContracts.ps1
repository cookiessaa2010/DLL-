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

if ($source -notmatch 'IsStartAllianceDecisionAllowedBetweenKingdoms') {
    throw 'Treaty creation must pass through the TOR kingdom permission model.'
}
if ($source -notmatch 'FactionManager.IsAtWarAgainstFaction') {
    throw 'Treaty creation must refuse kingdoms already at war.'
}

Write-Output 'KaiTOR Diplomacy contract tests: PASS'
Write-Output "  C# files: $($sourceFiles.Count)"
Write-Output '  TOR model ownership preserved.'
Write-Output '  No Harmony/model replacement/forced war-peace-marriage actions detected.'

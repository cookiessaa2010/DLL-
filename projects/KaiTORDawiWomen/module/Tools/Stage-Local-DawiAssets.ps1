[CmdletBinding()]
param(
    [Parameter(Mandatory=$true, Position=0)]
    [string]$ArmoryPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step([string]$text) {
    Write-Host "[KaiTOR Dawi] $text" -ForegroundColor Cyan
}

function Find-ActionSetsXml([string]$root) {
    $preferred = Join-Path $root 'ModuleData\action_sets.xml'
    if (Test-Path -LiteralPath $preferred -PathType Leaf) { return $preferred }

    $candidates = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter 'action_sets.xml' -ErrorAction SilentlyContinue)
    foreach ($file in $candidates) {
        if (Select-String -LiteralPath $file.FullName -Pattern 'as_dwarf_female_warrior' -SimpleMatch -Quiet) {
            return $file.FullName
        }
    }
    return $null
}

function Find-RaceTest([string]$root) {
    $preferred = Join-Path $root 'Assets\Race Test'
    if (Test-Path -LiteralPath $preferred -PathType Container) { return $preferred }

    $known = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter 'sk_dwarf_bm_f1_geo.tpac' -ErrorAction SilentlyContinue | Select-Object -First 1)
    if ($known.Count -gt 0) {
        $dwarf = Split-Path -Parent $known[0].FullName
        $raceTest = Split-Path -Parent $dwarf
        if ((Split-Path -Leaf $raceTest) -eq 'Race Test') { return $raceTest }
    }

    $dirs = @(Get-ChildItem -LiteralPath $root -Recurse -Directory -Filter 'Race Test' -ErrorAction SilentlyContinue | Select-Object -First 1)
    if ($dirs.Count -gt 0) { return $dirs[0].FullName }
    return $null
}

if (-not (Test-Path -LiteralPath $ArmoryPath -PathType Container)) {
    throw "LOTRLOME_Armory folder not found: $ArmoryPath"
}

$toolDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$moduleRoot = Split-Path -Parent $toolDir
$moduleData = Join-Path $moduleRoot 'ModuleData'
$targetAssets = Join-Path $moduleRoot 'Assets\Race Test'
$targetActionSets = Join-Path $moduleData 'action_sets.xml'
$readyMarker = Join-Path $moduleData 'kaitor_dawi_assets_ready.flag'
$reportPath = Join-Path $moduleData 'kaitor_dawi_stage_report.json'

if (Test-Path -LiteralPath $readyMarker) { Remove-Item -LiteralPath $readyMarker -Force }

Write-Step "Locating known female-Dawi resources by FILE NAME, not by scanning TPAC contents..."
$raceTest = Find-RaceTest $ArmoryPath
if (-not $raceTest) { throw 'Could not locate Assets\Race Test in the supplied Armory.' }

$bodyTpac = Join-Path $raceTest 'dwarf\sk_dwarf_bm_f1_geo.tpac'
$underwearTpac = Join-Path $raceTest 'dwarf\sk_dwarf_underwear_female_geo.tpac'
foreach ($required in @($bodyTpac, $underwearTpac)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required female-Dawi TPAC is missing: $required"
    }
}

$actionSetsSource = Find-ActionSetsXml $ArmoryPath
if (-not $actionSetsSource) { throw 'Could not locate action_sets.xml containing as_dwarf_female_warrior.' }

Write-Step "Extracting all as_dwarf_female_* action sets..."
[xml]$actionDoc = Get-Content -LiteralPath $actionSetsSource -Raw
$sourceRoot = $actionDoc.DocumentElement
if ($null -eq $sourceRoot) { throw 'Source action_sets.xml has no document element.' }

$selected = @($sourceRoot.action_set | Where-Object { $_.id -like 'as_dwarf_female_*' })
if ($selected.Count -eq 0) { throw 'No as_dwarf_female_* action sets were found.' }
if (-not ($selected | Where-Object { $_.id -eq 'as_dwarf_female_warrior' })) {
    throw 'Required as_dwarf_female_warrior action set is missing.'
}

$warrior = $selected | Where-Object { $_.id -eq 'as_dwarf_female_warrior' } | Select-Object -First 1
if ([string]$warrior.base_set -ne 'as_dwarf_warrior') {
    throw "Unexpected female dwarf base_set: '$($warrior.base_set)' (expected as_dwarf_warrior)."
}

$outDoc = New-Object System.Xml.XmlDocument
$decl = $outDoc.CreateXmlDeclaration('1.0','utf-8',$null)
[void]$outDoc.AppendChild($decl)
$outRoot = $outDoc.CreateElement('action_sets')
[void]$outDoc.AppendChild($outRoot)
foreach ($node in $selected) {
    [void]$outRoot.AppendChild($outDoc.ImportNode($node, $true))
}
$settings = New-Object System.Xml.XmlWriterSettings
$settings.Indent = $true
$settings.Encoding = New-Object System.Text.UTF8Encoding($false)
$writer = [System.Xml.XmlWriter]::Create($targetActionSets, $settings)
try { $outDoc.Save($writer) } finally { $writer.Dispose() }

Write-Step "Copying Assets\Race Test locally (straight file copy, no binary scan)..."
if (Test-Path -LiteralPath $targetAssets) { Remove-Item -LiteralPath $targetAssets -Recurse -Force }
New-Item -ItemType Directory -Path $targetAssets -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $raceTest '*') -Destination $targetAssets -Recurse -Force

$targetBody = Join-Path $targetAssets 'dwarf\sk_dwarf_bm_f1_geo.tpac'
$targetUnderwear = Join-Path $targetAssets 'dwarf\sk_dwarf_underwear_female_geo.tpac'
foreach ($required in @($targetBody, $targetUnderwear, $targetActionSets)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Staging verification failed, missing: $required"
    }
}

$report = [ordered]@{
    status = 'READY_FOR_MANUAL_RIG_TEST'
    source_armory = (Resolve-Path -LiteralPath $ArmoryPath).Path
    source_race_test = (Resolve-Path -LiteralPath $raceTest).Path
    action_sets_source = (Resolve-Path -LiteralPath $actionSetsSource).Path
    female_action_sets = @($selected | ForEach-Object { [string]$_.id })
    verified_tpacs = @(
        'Assets\\Race Test\\dwarf\\sk_dwarf_bm_f1_geo.tpac',
        'Assets\\Race Test\\dwarf\\sk_dwarf_underwear_female_geo.tpac'
    )
    automatic_population = $false
    created_utc = [DateTime]::UtcNow.ToString('o')
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding UTF8

@(
    'KaiTOR Dawi Women assets staged successfully.',
    'Mode: MANUAL RIG TEST ONLY',
    ('UTC: ' + [DateTime]::UtcNow.ToString('o'))
) | Set-Content -LiteralPath $readyMarker -Encoding ASCII

Write-Host ''
Write-Host 'READY_FOR_MANUAL_RIG_TEST' -ForegroundColor Green
Write-Host "Action sets copied: $($selected.Count)"
Write-Host "Report: $reportPath"
Write-Host 'Restart Bannerlord completely, then run: kaitor_dawi_women.status' -ForegroundColor Yellow

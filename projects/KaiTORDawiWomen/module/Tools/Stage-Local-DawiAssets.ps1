[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step([string]$text) {
    Write-Host "[KaiTOR Dawi] $text" -ForegroundColor Cyan
}

function Find-ExactFile([string]$root, [string]$fileName) {
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { return $null }
    try {
        foreach ($path in [System.IO.Directory]::EnumerateFiles($root, $fileName, [System.IO.SearchOption]::AllDirectories)) {
            return $path
        }
    }
    catch [System.UnauthorizedAccessException] { }
    catch [System.IO.DirectoryNotFoundException] { }
    return $null
}

function Find-XmlWithText([string]$root, [string]$fileName, [string]$needle) {
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { return $null }
    foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter $fileName -ErrorAction SilentlyContinue)) {
        try {
            if (Select-String -LiteralPath $file.FullName -Pattern $needle -SimpleMatch -Quiet -ErrorAction SilentlyContinue) {
                return $file.FullName
            }
        } catch { }
    }
    return $null
}

function Find-TorDwarfMaleActionSet([string]$modulesRoot) {
    $roots = @('TOR_Armory','TOR_Core') | ForEach-Object { Join-Path $modulesRoot $_ } | Where-Object { Test-Path -LiteralPath $_ -PathType Container }
    foreach ($root in $roots) {
        foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.xml' -ErrorAction SilentlyContinue)) {
            try {
                [xml]$doc = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction Stop
                $node = $doc.SelectSingleNode("//Monster[@id='dwarf']")
                if ($node -and $node.HasAttribute('action_set')) {
                    return [string]$node.GetAttribute('action_set')
                }
            } catch { }
        }
    }
    return $null
}

function Write-XmlNoBom([System.Xml.XmlDocument]$doc, [string]$path) {
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $writer = [System.Xml.XmlWriter]::Create($path, $settings)
    try { $doc.Save($writer) } finally { $writer.Dispose() }
}

$toolDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$moduleRoot = Split-Path -Parent $toolDir
$modulesRoot = Split-Path -Parent $moduleRoot
$moduleData = Join-Path $moduleRoot 'ModuleData'
$targetAssets = Join-Path $moduleRoot 'Assets\Race Test'
$targetActionSets = Join-Path $moduleData 'action_sets.xml'
$targetSkinXslt = Join-Path $moduleData 'skins.xslt'
$targetMonster = Join-Path $moduleData 'kaitor_dawi_monster.xml'
$readyMarker = Join-Path $moduleData 'kaitor_dawi_assets_ready.flag'
$reportPath = Join-Path $moduleData 'kaitor_dawi_stage_report.json'

if (Test-Path -LiteralPath $readyMarker) { Remove-Item -LiteralPath $readyMarker -Force }

Write-Step 'Searching Bannerlord Modules for exact female-Dawi filenames. TPAC contents are NOT scanned.'
Write-Step "Modules root: $modulesRoot"

$bodyTpac = Find-ExactFile $modulesRoot 'sk_dwarf_bm_f1_geo.tpac'
$underwearTpac = Find-ExactFile $modulesRoot 'sk_dwarf_underwear_female_geo.tpac'

if (-not $bodyTpac) { throw 'sk_dwarf_bm_f1_geo.tpac was not found anywhere under Bannerlord\Modules.' }
if (-not $underwearTpac) { throw 'sk_dwarf_underwear_female_geo.tpac was not found anywhere under Bannerlord\Modules.' }

$bodyDir = Split-Path -Parent $bodyTpac
$underwearDir = Split-Path -Parent $underwearTpac
if ($bodyDir -ne $underwearDir) {
    throw 'Female dwarf body and underwear TPACs were found in different folders. Refusing automatic staging.'
}
$sourceDwarfDir = $bodyDir
$sourceRaceTest = Split-Path -Parent $sourceDwarfDir

Write-Step "Body TPAC: $bodyTpac"
Write-Step "Underwear TPAC: $underwearTpac"

$sourceModuleRoot = $null
$current = $sourceDwarfDir
while ($current) {
    if (Test-Path -LiteralPath (Join-Path $current 'SubModule.xml') -PathType Leaf) {
        $sourceModuleRoot = $current
        break
    }
    $parent = Split-Path -Parent $current
    if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) { break }
    $current = $parent
}
if (-not $sourceModuleRoot) {
    $assetsDir = Split-Path -Parent $sourceRaceTest
    $sourceModuleRoot = Split-Path -Parent $assetsDir
}
Write-Step "Candidate source module: $sourceModuleRoot"

$actionSetsSource = Find-XmlWithText $sourceModuleRoot 'action_sets.xml' 'as_dwarf_female_warrior'
$skinsSource = Find-XmlWithText $sourceModuleRoot 'skins.xml' 'sk_dwarf_bm_f1_body'

if (-not $actionSetsSource) {
    $actionSetsSource = Find-XmlWithText $modulesRoot 'action_sets.xml' 'as_dwarf_female_warrior'
}
if (-not $skinsSource) {
    $skinsSource = Find-XmlWithText $modulesRoot 'skins.xml' 'sk_dwarf_bm_f1_body'
}

if (-not $actionSetsSource) { throw 'No action_sets.xml containing as_dwarf_female_warrior was found under Bannerlord\Modules.' }
if (-not $skinsSource) { throw 'No skins.xml containing sk_dwarf_bm_f1_body was found under Bannerlord\Modules.' }

Write-Step 'Reading female dwarf skin from installed source...'
[xml]$skinDoc = Get-Content -LiteralPath $skinsSource -Raw
$femaleSkin = $skinDoc.SelectSingleNode("//race[@id='dwarf']/skin[@gender='1' and @name='woman' and @mesh_maturity_type='adult']")
if (-not $femaleSkin) { throw 'Adult dwarf woman skin was not found in source skins.xml.' }
if ($femaleSkin.GetAttribute('body_meta_mesh') -ne 'sk_dwarf_bm_f1_body') {
    throw "Unexpected female dwarf body mesh: $($femaleSkin.GetAttribute('body_meta_mesh'))"
}
if ($femaleSkin.GetAttribute('underwear_bottom_mesh') -ne 'sk_dwarf_underwear_female_a') {
    throw "Unsafe female dwarf underwear mesh: $($femaleSkin.GetAttribute('underwear_bottom_mesh'))"
}

Write-Step 'Resolving TOR male Dawi action set...'
$torMaleActionSet = Find-TorDwarfMaleActionSet $modulesRoot
if ([string]::IsNullOrWhiteSpace($torMaleActionSet)) {
    throw 'Could not resolve TOR Monster.dwarf action_set. No changes were activated.'
}
Write-Step "TOR male Dawi action set: $torMaleActionSet"

[xml]$actionDoc = Get-Content -LiteralPath $actionSetsSource -Raw
$femaleWarrior = $actionDoc.SelectSingleNode("//action_set[@id='as_dwarf_female_warrior']")
if (-not $femaleWarrior) { throw 'as_dwarf_female_warrior is missing from source action_sets.xml.' }
$femaleWarrior.SetAttribute('base_set', $torMaleActionSet)

$outDoc = New-Object System.Xml.XmlDocument
$decl = $outDoc.CreateXmlDeclaration('1.0','utf-8',$null)
[void]$outDoc.AppendChild($decl)
$outRoot = $outDoc.CreateElement('action_sets')
[void]$outDoc.AppendChild($outRoot)
[void]$outRoot.AppendChild($outDoc.ImportNode($femaleWarrior, $true))
Write-XmlNoBom $outDoc $targetActionSets

Write-Step 'Building skin patch from installed female dwarf skin...'
$femaleSkinXml = $femaleSkin.OuterXml
$xslt = @"
<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:output method="xml" indent="yes" />
  <xsl:template match="@*|node()">
    <xsl:copy><xsl:apply-templates select="@*|node()" /></xsl:copy>
  </xsl:template>
  <xsl:template match="race[@id='dwarf']/skin[@gender='1' and @name='woman' and @mesh_maturity_type='adult']">
    $femaleSkinXml
  </xsl:template>
</xsl:stylesheet>
"@
[System.IO.File]::WriteAllText($targetSkinXslt, $xslt, (New-Object System.Text.UTF8Encoding($false)))

$monsterXml = @"
<?xml version="1.0" encoding="utf-8"?>
<Monsters>
  <Monster id="dwarf" female_action_set="as_dwarf_female_warrior" />
</Monsters>
"@
[System.IO.File]::WriteAllText($targetMonster, $monsterXml, (New-Object System.Text.UTF8Encoding($false)))

Write-Step 'Copying only the source dwarf asset folder. No TPAC content scan.'
$targetDwarfDir = Join-Path $targetAssets 'dwarf'
if (Test-Path -LiteralPath $targetAssets) { Remove-Item -LiteralPath $targetAssets -Recurse -Force }
New-Item -ItemType Directory -Path $targetDwarfDir -Force | Out-Null
Copy-Item -Path (Join-Path $sourceDwarfDir '*') -Destination $targetDwarfDir -Recurse -Force

$targetBody = Join-Path $targetDwarfDir 'sk_dwarf_bm_f1_geo.tpac'
$targetUnderwear = Join-Path $targetDwarfDir 'sk_dwarf_underwear_female_geo.tpac'
foreach ($required in @($targetBody, $targetUnderwear, $targetActionSets, $targetSkinXslt, $targetMonster)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Staging verification failed, missing: $required"
    }
}

$report = [ordered]@{
    status = 'READY_FOR_MANUAL_RIG_TEST'
    source_body_tpac = $bodyTpac
    source_underwear_tpac = $underwearTpac
    source_dwarf_dir = $sourceDwarfDir
    skins_source = $skinsSource
    action_sets_source = $actionSetsSource
    tor_male_action_set = $torMaleActionSet
    female_action_set = 'as_dwarf_female_warrior'
    female_body_mesh = $femaleSkin.GetAttribute('body_meta_mesh')
    female_underwear_mesh = $femaleSkin.GetAttribute('underwear_bottom_mesh')
    automatic_population = $false
    created_utc = [DateTime]::UtcNow.ToString('o')
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding UTF8

@(
    'KaiTOR Dawi Women assets staged successfully.',
    'Mode: MANUAL RIG TEST ONLY',
    ('TOR male action set preserved: ' + $torMaleActionSet),
    ('UTC: ' + [DateTime]::UtcNow.ToString('o'))
) | Set-Content -LiteralPath $readyMarker -Encoding ASCII

Write-Host ''
Write-Host 'READY_FOR_MANUAL_RIG_TEST' -ForegroundColor Green
Write-Host "TOR male Dawi action set preserved: $torMaleActionSet"
Write-Host "Report: $reportPath"
Write-Host 'Restart Bannerlord completely, then run: kaitor_dawi_women.status' -ForegroundColor Yellow

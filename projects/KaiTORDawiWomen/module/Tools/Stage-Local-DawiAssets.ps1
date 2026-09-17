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

function Find-XmlWithText([string]$root, [string]$fileName, [string]$needle) {
    $preferred = Join-Path $root ("ModuleData\" + $fileName)
    if ((Test-Path -LiteralPath $preferred -PathType Leaf) -and
        (Select-String -LiteralPath $preferred -Pattern $needle -SimpleMatch -Quiet)) {
        return $preferred
    }

    foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter $fileName -ErrorAction SilentlyContinue)) {
        if (Select-String -LiteralPath $file.FullName -Pattern $needle -SimpleMatch -Quiet) {
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

function Find-TorDwarfMaleActionSet([string]$modulesRoot) {
    $roots = @('TOR_Armory','TOR_Core') | ForEach-Object { Join-Path $modulesRoot $_ } | Where-Object { Test-Path -LiteralPath $_ -PathType Container }
    foreach ($root in $roots) {
        foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.xml' -ErrorAction SilentlyContinue)) {
            if (-not (Select-String -LiteralPath $file.FullName -Pattern 'Monster id="dwarf"' -SimpleMatch -Quiet)) { continue }
            try {
                [xml]$doc = Get-Content -LiteralPath $file.FullName -Raw
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

if (-not (Test-Path -LiteralPath $ArmoryPath -PathType Container)) {
    throw "LOTRLOME_Armory folder not found: $ArmoryPath"
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

Write-Step 'Locating female-Dawi files by path/name only; TPAC contents are NOT scanned...'
$raceTest = Find-RaceTest $ArmoryPath
if (-not $raceTest) { throw 'Could not locate the Armory Race Test asset directory.' }

$bodyTpac = Join-Path $raceTest 'dwarf\sk_dwarf_bm_f1_geo.tpac'
$underwearTpac = Join-Path $raceTest 'dwarf\sk_dwarf_underwear_female_geo.tpac'
foreach ($required in @($bodyTpac, $underwearTpac)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required female-Dawi TPAC is missing: $required"
    }
}

$actionSetsSource = Find-XmlWithText $ArmoryPath 'action_sets.xml' 'as_dwarf_female_warrior'
if (-not $actionSetsSource) { throw 'Could not locate action_sets.xml containing as_dwarf_female_warrior.' }
$skinsSource = Find-XmlWithText $ArmoryPath 'skins.xml' 'sk_dwarf_bm_f1_body'
if (-not $skinsSource) { throw 'Could not locate skins.xml containing the real female dwarf skin.' }

Write-Step 'Reading the exact female skin from YOUR Armory version...'
[xml]$skinDoc = Get-Content -LiteralPath $skinsSource -Raw
$femaleSkin = $skinDoc.SelectSingleNode("//race[@id='dwarf']/skin[@gender='1' and @name='woman' and @mesh_maturity_type='adult']")
if (-not $femaleSkin) { throw 'Adult dwarf woman skin was not found in source skins.xml.' }
if ($femaleSkin.GetAttribute('body_meta_mesh') -ne 'sk_dwarf_bm_f1_body') {
    throw "Unexpected female dwarf body mesh: $($femaleSkin.GetAttribute('body_meta_mesh'))"
}
if ($femaleSkin.GetAttribute('underwear_bottom_mesh') -ne 'sk_dwarf_underwear_female_a') {
    throw "Unsafe female dwarf underwear mesh: $($femaleSkin.GetAttribute('underwear_bottom_mesh'))"
}

Write-Step 'Resolving TOR male Dawi action set so male dwarfs stay untouched...'
$torMaleActionSet = Find-TorDwarfMaleActionSet $modulesRoot
if ([string]::IsNullOrWhiteSpace($torMaleActionSet)) {
    throw 'Could not resolve TOR Monster.dwarf action_set. No changes were activated.'
}

Write-Step "TOR male Dawi action set: $torMaleActionSet"
[xml]$actionDoc = Get-Content -LiteralPath $actionSetsSource -Raw
$femaleWarrior = $actionDoc.SelectSingleNode("//action_set[@id='as_dwarf_female_warrior']")
if (-not $femaleWarrior) { throw 'as_dwarf_female_warrior is missing from source action_sets.xml.' }

# Rebase ONLY our copied female set onto TOR's current male dwarf action set.
# The original TOR action set itself is never copied or modified.
$femaleWarrior.SetAttribute('base_set', $torMaleActionSet)
$outDoc = New-Object System.Xml.XmlDocument
$decl = $outDoc.CreateXmlDeclaration('1.0','utf-8',$null)
[void]$outDoc.AppendChild($decl)
$outRoot = $outDoc.CreateElement('action_sets')
[void]$outDoc.AppendChild($outRoot)
[void]$outRoot.AppendChild($outDoc.ImportNode($femaleWarrior, $true))
Write-XmlNoBom $outDoc $targetActionSets

Write-Step 'Building an XSLT that replaces ONLY TOR adult dwarf woman with the exact local Armory woman skin...'
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

Write-Step 'Activating female_action_set on Monster.dwarf without changing TOR male action_set...'
$monsterXml = @"
<?xml version="1.0" encoding="utf-8"?>
<Monsters>
  <Monster id="dwarf" female_action_set="as_dwarf_female_warrior" />
</Monsters>
"@
[System.IO.File]::WriteAllText($targetMonster, $monsterXml, (New-Object System.Text.UTF8Encoding($false)))

Write-Step 'Copying Assets\Race Test locally (direct copy, no content scan)...'
if (Test-Path -LiteralPath $targetAssets) { Remove-Item -LiteralPath $targetAssets -Recurse -Force }
New-Item -ItemType Directory -Path $targetAssets -Force | Out-Null
Copy-Item -Path (Join-Path $raceTest '*') -Destination $targetAssets -Recurse -Force

$targetBody = Join-Path $targetAssets 'dwarf\sk_dwarf_bm_f1_geo.tpac'
$targetUnderwear = Join-Path $targetAssets 'dwarf\sk_dwarf_underwear_female_geo.tpac'
foreach ($required in @($targetBody, $targetUnderwear, $targetActionSets, $targetSkinXslt, $targetMonster)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Staging verification failed, missing: $required"
    }
}

$report = [ordered]@{
    status = 'READY_FOR_MANUAL_RIG_TEST'
    source_armory = (Resolve-Path -LiteralPath $ArmoryPath).Path
    source_race_test = (Resolve-Path -LiteralPath $raceTest).Path
    skins_source = (Resolve-Path -LiteralPath $skinsSource).Path
    action_sets_source = (Resolve-Path -LiteralPath $actionSetsSource).Path
    tor_male_action_set = $torMaleActionSet
    female_action_set = 'as_dwarf_female_warrior'
    female_body_mesh = $femaleSkin.GetAttribute('body_meta_mesh')
    female_underwear_mesh = $femaleSkin.GetAttribute('underwear_bottom_mesh')
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
    ('TOR male action set preserved: ' + $torMaleActionSet),
    ('UTC: ' + [DateTime]::UtcNow.ToString('o'))
) | Set-Content -LiteralPath $readyMarker -Encoding ASCII

Write-Host ''
Write-Host 'READY_FOR_MANUAL_RIG_TEST' -ForegroundColor Green
Write-Host "TOR male Dawi action set preserved: $torMaleActionSet"
Write-Host "Report: $reportPath"
Write-Host 'Restart Bannerlord completely, then run: kaitor_dawi_women.status' -ForegroundColor Yellow

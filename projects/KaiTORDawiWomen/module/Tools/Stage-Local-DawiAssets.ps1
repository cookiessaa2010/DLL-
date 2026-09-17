[CmdletBinding()]
param(
    [Parameter(Mandatory=$false, Position=0)]
    [string]$SearchRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step([string]$text) {
    Write-Host "[KaiTOR Dawi] $text" -ForegroundColor Cyan
}

function Add-UniqueRoot([System.Collections.Generic.List[string]]$list, [string]$path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return }
    try { $full = [System.IO.Path]::GetFullPath($path) } catch { return }
    if (-not (Test-Path -LiteralPath $full -PathType Container)) { return }
    if (-not ($list -contains $full)) { [void]$list.Add($full) }
}

function Find-ExactFileFast([string[]]$roots, [string]$fileName) {
    $where = Join-Path $env:SystemRoot 'System32\where.exe'
    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }

        foreach ($candidate in @(
            (Join-Path $root $fileName),
            (Join-Path $root ("Assets\Race Test\dwarf\" + $fileName)),
            (Join-Path $root ("Race Test\dwarf\" + $fileName)),
            (Join-Path $root ("LOTRLOME_Armory\Assets\Race Test\dwarf\" + $fileName))
        )) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
        }

        if (Test-Path -LiteralPath $where -PathType Leaf) {
            $hit = @(& $where /r $root $fileName 2>$null | Select-Object -First 1)
            if ($hit.Count -gt 0 -and (Test-Path -LiteralPath $hit[0] -PathType Leaf)) {
                return [string]$hit[0]
            }
        } else {
            $hit = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter $fileName -ErrorAction SilentlyContinue | Select-Object -First 1)
            if ($hit.Count -gt 0) { return $hit[0].FullName }
        }
    }
    return $null
}

function Find-AncestorNamed([string]$path, [string]$name) {
    $item = Get-Item -LiteralPath $path
    $dir = if ($item.PSIsContainer) { $item } else { $item.Directory }
    while ($null -ne $dir) {
        if ($dir.Name -eq $name) { return $dir.FullName }
        $dir = $dir.Parent
    }
    return $null
}

function Find-XmlWithText([string[]]$roots, [string]$fileName, [string]$needle) {
    foreach ($root in $roots) {
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
    }
    return $null
}

function Find-TorDwarfMaleActionSet([string]$modulesRoot) {
    $roots = @('TOR_Armory','TOR_Core') | ForEach-Object { Join-Path $modulesRoot $_ } | Where-Object { Test-Path -LiteralPath $_ -PathType Container }
    foreach ($root in $roots) {
        foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.xml' -ErrorAction SilentlyContinue)) {
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

$roots = New-Object 'System.Collections.Generic.List[string]'
Add-UniqueRoot $roots $SearchRoot
foreach ($name in @('LOTRLOME_Armory','TAOM','TOR_Armory','TOR_Core')) {
    Add-UniqueRoot $roots (Join-Path $modulesRoot $name)
}
Add-UniqueRoot $roots $modulesRoot

Write-Step 'Searching exact female-Dawi filenames; TPAC contents are NOT scanned...'
Write-Step ('Roots: ' + ($roots -join ' | '))

$bodyTpac = Find-ExactFileFast $roots.ToArray() 'sk_dwarf_bm_f1_geo.tpac'
if (-not $bodyTpac) {
    throw @"
Required file sk_dwarf_bm_f1_geo.tpac was not found anywhere in the supplied folder or Bannerlord\Modules.
This means the LOTRLOME/TAOM female-Dwarf art package is not installed locally (TOR_Armory alone is not enough for this test).
Nothing was activated. If you have LOTRLOME_Armory elsewhere, drag THAT folder onto SETUP-DAWI-ASSETS.cmd.
"@
}

$raceTest = Find-AncestorNamed $bodyTpac 'Race Test'
if (-not $raceTest) { throw "Found female body TPAC, but it is not inside a 'Race Test' directory: $bodyTpac" }

$underwearTpac = Join-Path $raceTest 'dwarf\sk_dwarf_underwear_female_geo.tpac'
if (-not (Test-Path -LiteralPath $underwearTpac -PathType Leaf)) {
    throw "Female body TPAC was found, but underwear TPAC is missing beside it: $underwearTpac"
}

$assetsDir = Split-Path -Parent $raceTest
$sourceModule = Split-Path -Parent $assetsDir
if ((Split-Path -Leaf $assetsDir) -ne 'Assets') {
    $sourceModule = Split-Path -Parent $raceTest
}

Write-Step "Female Dawi assets found in: $sourceModule"
$xmlRoots = New-Object 'System.Collections.Generic.List[string]'
Add-UniqueRoot $xmlRoots $sourceModule
Add-UniqueRoot $xmlRoots $SearchRoot

$actionSetsSource = Find-XmlWithText $xmlRoots.ToArray() 'action_sets.xml' 'as_dwarf_female_warrior'
if (-not $actionSetsSource) { throw 'Female assets were found, but action_sets.xml with as_dwarf_female_warrior is missing.' }
$skinsSource = Find-XmlWithText $xmlRoots.ToArray() 'skins.xml' 'sk_dwarf_bm_f1_body'
if (-not $skinsSource) { throw 'Female assets were found, but skins.xml with sk_dwarf_bm_f1_body is missing.' }

Write-Step 'Reading exact female skin from the local Armory version...'
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
$femaleWarrior.SetAttribute('base_set', $torMaleActionSet)

$outDoc = New-Object System.Xml.XmlDocument
$decl = $outDoc.CreateXmlDeclaration('1.0','utf-8',$null)
[void]$outDoc.AppendChild($decl)
$outRoot = $outDoc.CreateElement('action_sets')
[void]$outDoc.AppendChild($outRoot)
[void]$outRoot.AppendChild($outDoc.ImportNode($femaleWarrior, $true))
Write-XmlNoBom $outDoc $targetActionSets

Write-Step 'Building skin override for ONLY the adult dwarf woman...'
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

Write-Step 'Copying Race Test assets locally (direct filesystem copy, no binary scan)...'
if (Test-Path -LiteralPath $targetAssets) { Remove-Item -LiteralPath $targetAssets -Recurse -Force }
New-Item -ItemType Directory -Path $targetAssets -Force | Out-Null
Copy-Item -Path (Join-Path $raceTest '*') -Destination $targetAssets -Recurse -Force

foreach ($required in @(
    (Join-Path $targetAssets 'dwarf\sk_dwarf_bm_f1_geo.tpac'),
    (Join-Path $targetAssets 'dwarf\sk_dwarf_underwear_female_geo.tpac'),
    $targetActionSets, $targetSkinXslt, $targetMonster
)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Staging verification failed, missing: $required"
    }
}

$report = [ordered]@{
    status = 'READY_FOR_MANUAL_RIG_TEST'
    search_root = $SearchRoot
    source_module = (Resolve-Path -LiteralPath $sourceModule).Path
    source_race_test = (Resolve-Path -LiteralPath $raceTest).Path
    skins_source = (Resolve-Path -LiteralPath $skinsSource).Path
    action_sets_source = (Resolve-Path -LiteralPath $actionSetsSource).Path
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
    ('Source module: ' + $sourceModule),
    ('TOR male action set preserved: ' + $torMaleActionSet),
    ('UTC: ' + [DateTime]::UtcNow.ToString('o'))
) | Set-Content -LiteralPath $readyMarker -Encoding ASCII

Write-Host ''
Write-Host 'READY_FOR_MANUAL_RIG_TEST' -ForegroundColor Green
Write-Host "Source: $sourceModule"
Write-Host "TOR male Dawi action set preserved: $torMaleActionSet"
Write-Host "Report: $reportPath"
Write-Host 'Restart Bannerlord completely, then run: kaitor_dawi_women.status' -ForegroundColor Yellow

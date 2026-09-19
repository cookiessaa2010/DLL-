[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runtime = Resolve-Path (Join-Path $PSScriptRoot '..\runtime')
$cleavePreflight = Join-Path $runtime 'Test-KaiTORCleaveCompatibility.ps1'
$stabilityPreflight = Join-Path $runtime 'Test-KaiTORStabilityCompatibility.ps1'
$portraitPreflight = Join-Path $runtime 'Test-KaiTORPortraitFixCompatibility.ps1'
$torLauncher = Join-Path $runtime 'Start-KaiTORTorCampaignServer.ps1'

function Write-Module {
    param([string]$Root,[string]$Id,[string]$Xml,[string[]]$Dlls=@())
    $module = Join-Path $Root ("Modules\{0}" -f $Id)
    New-Item -ItemType Directory -Force -Path $module | Out-Null
    Set-Content -LiteralPath (Join-Path $module 'SubModule.xml') -Value $Xml -Encoding UTF8
    if ($Dlls.Count -gt 0) {
        $bin = Join-Path $module 'bin\Win64_Shipping_Client'
        New-Item -ItemType Directory -Force -Path $bin | Out-Null
        foreach ($dll in $Dlls) {
            Set-Content -LiteralPath (Join-Path $bin $dll) -Value 'CI placeholder' -Encoding ASCII
        }
    }
}

function Get-HarmonyXml {
@'
<Module><Id value="Bannerlord.Harmony"/><Version value="v2.4.2.248"/><ModuleCategory value="Singleplayer"/><SubModules><SubModule><DLLName value="Bannerlord.Harmony.dll"/></SubModule></SubModules></Module>
'@
}

function Get-CleaveXml {
@'
<Module><Id value="KaiCleave"/><Version value="v1.3.15.31"/><ModuleCategory value="Singleplayer"/><DependedModules><DependedModule Id="Bannerlord.Harmony" DependentVersion="v2.4.2.248"/><DependedModule Id="Native" DependentVersion="v1.3.15"/><DependedModule Id="SandBoxCore" DependentVersion="v1.3.15"/><DependedModule Id="Sandbox" DependentVersion="v1.3.15"/></DependedModules><SubModules><SubModule><DLLName value="KaiCleave.dll"/></SubModule></SubModules></Module>
'@
}

function Get-StabilityXml {
@'
<Module><Id value="KaiTOR_Stability"/><Version value="v1.3.15.50"/><SingleplayerModule value="true"/><MultiplayerModule value="false"/><DependedModules><DependedModule Id="Native" DependentVersion="v1.3.15"/><DependedModule Id="TOR_Core" DependentVersion="v1.3.15"/></DependedModules><SubModules><SubModule><DLLName value="KaiTORStability.dll"/></SubModule></SubModules></Module>
'@
}

function Get-PortraitXml {
@'
<Module><Id value="KaiTOR_PortraitFix"/><Version value="v1.3.15.60"/><SingleplayerModule value="true"/><MultiplayerModule value="false"/><ModuleCategory value="Singleplayer"/><DependedModules><DependedModule Id="Bannerlord.Harmony" DependentVersion="v2.4.2.248"/><DependedModule Id="Native" DependentVersion="v1.3.15"/><DependedModule Id="SandBoxCore" DependentVersion="v1.3.15"/><DependedModule Id="Sandbox" DependentVersion="v1.3.15"/></DependedModules><SubModules><SubModule><DLLName value="KaiTORPortraitFix.dll"/></SubModule></SubModules></Module>
'@
}

function Initialize-BaseRoot {
    param([string]$Root)
    New-Item -ItemType Directory -Force -Path (Join-Path $Root 'Modules') | Out-Null
    $bin = Join-Path $Root 'bin\Win64_Shipping_Client'
    New-Item -ItemType Directory -Force -Path $bin | Out-Null
    Set-Content -LiteralPath (Join-Path $bin 'Bannerlord.exe') -Value 'CI placeholder' -Encoding ASCII

    foreach ($id in @('Native','SandBoxCore','Sandbox','CustomBattle','StoryMode','TOR_Armory','TOR_Environment')) {
        $xml = '<Module><Id value="{0}"/><Version value="v1.3.15"/></Module>' -f $id
        Write-Module -Root $Root -Id $id -Xml $xml
    }

    Write-Module -Root $Root -Id 'TOR_Core' -Dlls @('TOR_Core.dll') -Xml @'
<Module><Id value="TOR_Core"/><Version value="v1.3.15"/><DependedModules><DependedModule Id="Native" DependentVersion="v1.3.15"/><DependedModule Id="SandBoxCore" DependentVersion="v1.3.15"/><DependedModule Id="Sandbox" DependentVersion="v1.3.15"/><DependedModule Id="TOR_Armory" DependentVersion="v1.3.15"/><DependedModule Id="TOR_Environment" DependentVersion="v1.3.15"/></DependedModules><SubModules><SubModule><DLLName value="TOR_Core.dll"/><Tags><Tag key="DedicatedServerType" value="none"/><Tag key="IsNoRenderModeElement" value="false"/></Tags></SubModule></SubModules></Module>
'@

    Write-Module -Root $Root -Id 'Coop' -Dlls @('Common.dll','Coop.dll','Coop.Core.dll','Coop.Steam.dll','GameInterface.dll','Missions.dll') -Xml @'
<Module><Id value="Coop"/><Version value="v1.3.15.13"/><DependedModules><DependedModule Id="Native" DependentVersion="v1.3.15"/><DependedModule Id="SandBoxCore" DependentVersion="v1.3.15"/><DependedModule Id="Sandbox" DependentVersion="v1.3.15"/><DependedModule Id="CustomBattle" DependentVersion="v1.3.15"/><DependedModule Id="StoryMode" DependentVersion="v1.3.15"/></DependedModules></Module>
'@
}

function Install-KaiCompanions {
    param([string]$Root)
    Write-Module -Root $Root -Id 'Bannerlord.Harmony' -Dlls @('Bannerlord.Harmony.dll') -Xml (Get-HarmonyXml)
    Write-Module -Root $Root -Id 'KaiCleave' -Dlls @('KaiCleave.dll') -Xml (Get-CleaveXml)
    Write-Module -Root $Root -Id 'KaiTOR_Stability' -Dlls @('KaiTORStability.dll') -Xml (Get-StabilityXml)
    Write-Module -Root $Root -Id 'KaiTOR_PortraitFix' -Dlls @('KaiTORPortraitFix.dll') -Xml (Get-PortraitXml)
}

function Write-WorkshopItem {
    param([string]$WorkshopRoot,[string]$Folder,[string]$Xml,[string[]]$Dlls=@())
    $dir = Join-Path $WorkshopRoot $Folder
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Set-Content -LiteralPath (Join-Path $dir 'SubModule.xml') -Value $Xml -Encoding UTF8
    if ($Dlls.Count -gt 0) {
        $bin = Join-Path $dir 'bin\Win64_Shipping_Client'
        New-Item -ItemType Directory -Force -Path $bin | Out-Null
        foreach ($dll in $Dlls) {
            Set-Content -LiteralPath (Join-Path $bin $dll) -Value 'CI placeholder' -Encoding ASCII
        }
    }
}

function Assert-Contains {
    param([string]$Text,[string]$Needle)
    if ($Text -notmatch [regex]::Escape($Needle)) { throw "Missing expected marker: $Needle" }
}

$manualRoot = Join-Path $env:RUNNER_TEMP 'kaitor-steam-companions-manual'
Remove-Item -LiteralPath $manualRoot -Recurse -Force -ErrorAction SilentlyContinue
Initialize-BaseRoot -Root $manualRoot
Install-KaiCompanions -Root $manualRoot

foreach ($check in @(
    @{ Path=$cleavePreflight; Marker='KaiCleave compatibility preflight: PASS' },
    @{ Path=$stabilityPreflight; Marker='KaiTOR Stability compatibility preflight: PASS' },
    @{ Path=$portraitPreflight; Marker='KaiTOR Portrait Fix compatibility preflight: PASS' }
)) {
    $text = (@(& $check.Path -BannerlordRoot $manualRoot 3>&1) | Out-String)
    Write-Host $text
    Assert-Contains -Text $text -Needle $check.Marker
}

$expectedOrder = 'Bannerlord.Harmony, Native, SandBoxCore, Sandbox, CustomBattle, StoryMode, TOR_Armory, TOR_Environment, TOR_Core, KaiCleave, KaiTOR_Stability, KaiTOR_PortraitFix, Coop'
$manualLaunch = (& $torLauncher -BannerlordRoot $manualRoot -SaveName 'KaiTOR-Steam-Manual-CI' -SkipVersionCheck -DryRun 3>&1 2>&1 | Out-String)
Write-Host $manualLaunch
foreach ($marker in @('KaiCleave integration: ACTIVE (v1.3.15.31).','KaiTOR Stability integration: ACTIVE (v1.3.15.50).','KaiTOR Portrait Fix integration: ACTIVE (v1.3.15.60).',$expectedOrder,'DRY RUN: process not started')) {
    Assert-Contains -Text $manualLaunch -Needle $marker
}

$workshopRoot = Join-Path $env:RUNNER_TEMP 'kaitor-steam-workshop-261550'
$workshopBannerlord = Join-Path $env:RUNNER_TEMP 'kaitor-steam-companions-workshop'
Remove-Item -LiteralPath $workshopRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $workshopBannerlord -Recurse -Force -ErrorAction SilentlyContinue
Initialize-BaseRoot -Root $workshopBannerlord
New-Item -ItemType Directory -Force -Path $workshopRoot | Out-Null

Write-WorkshopItem -WorkshopRoot $workshopRoot -Folder '1001' -Xml (Get-HarmonyXml) -Dlls @('Bannerlord.Harmony.dll')
Write-WorkshopItem -WorkshopRoot $workshopRoot -Folder '1002' -Xml (Get-CleaveXml) -Dlls @('KaiCleave.dll')
Write-WorkshopItem -WorkshopRoot $workshopRoot -Folder '1003' -Xml (Get-StabilityXml) -Dlls @('KaiTORStability.dll')
Write-WorkshopItem -WorkshopRoot $workshopRoot -Folder '1004' -Xml (Get-PortraitXml) -Dlls @('KaiTORPortraitFix.dll')

$workshopLaunch = (& $torLauncher -BannerlordRoot $workshopBannerlord -WorkshopRoot $workshopRoot -SaveName 'KaiTOR-Steam-Workshop-CI' -SkipVersionCheck -DryRun 3>&1 2>&1 | Out-String)
Write-Host $workshopLaunch
foreach ($marker in @('Kai Workshop staging: KaiCleave v1.3.15.31','Kai Workshop staging: KaiTOR_Stability v1.3.15.50','Kai Workshop staging: KaiTOR_PortraitFix v1.3.15.60','Kai Workshop staging: Bannerlord.Harmony v2.4.2.248',$expectedOrder,'Workshop staging cleanup: PASS')) {
    Assert-Contains -Text $workshopLaunch -Needle $marker
}

foreach ($id in @('Bannerlord.Harmony','KaiCleave','KaiTOR_Stability','KaiTOR_PortraitFix')) {
    if (Test-Path -LiteralPath (Join-Path $workshopBannerlord ("Modules\{0}" -f $id))) {
        throw "Workshop cleanup left a staged module behind: $id"
    }
}

Write-Host 'KaiTOR Steam companion compatibility contracts: PASS'

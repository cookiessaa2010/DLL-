[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BannerlordRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ExpectedModuleId = 'KaiTOR_PortraitFix'
$ExpectedVersion = 'v1.3.15.60'
$ExpectedGameVersion = 'v1.3.15'
$ExpectedHarmonyVersion = 'v2.4.2.248'

$root = (Resolve-Path -LiteralPath $BannerlordRoot).Path
$moduleRoot = Join-Path $root 'Modules\KaiTOR_PortraitFix'
$metadataPath = Join-Path $moduleRoot 'SubModule.xml'
$runtimeDll = Join-Path $moduleRoot 'bin\Win64_Shipping_Client\KaiTORPortraitFix.dll'
$harmonyRoot = Join-Path $root 'Modules\Bannerlord.Harmony'
$harmonyMetadataPath = Join-Path $harmonyRoot 'SubModule.xml'
$harmonyRuntimeDll = Join-Path $harmonyRoot 'bin\Win64_Shipping_Client\Bannerlord.Harmony.dll'

if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
    throw "KaiTOR Portrait Fix metadata missing: $metadataPath"
}

try {
    [xml]$xml = Get-Content -LiteralPath $metadataPath -Raw
}
catch {
    throw "KaiTOR Portrait Fix SubModule.xml is invalid: $($_.Exception.Message)"
}

$idNode = $xml.SelectSingleNode('/Module/Id')
$versionNode = $xml.SelectSingleNode('/Module/Version')
$singleNode = $xml.SelectSingleNode('/Module/SingleplayerModule')
$multiNode = $xml.SelectSingleNode('/Module/MultiplayerModule')
$categoryNode = $xml.SelectSingleNode('/Module/ModuleCategory')

$id = if ($null -eq $idNode) { '' } else { [string]$idNode.GetAttribute('value') }
$version = if ($null -eq $versionNode) { '' } else { [string]$versionNode.GetAttribute('value') }
$single = if ($null -eq $singleNode) { '' } else { [string]$singleNode.GetAttribute('value') }
$multi = if ($null -eq $multiNode) { '' } else { [string]$multiNode.GetAttribute('value') }
$category = if ($null -eq $categoryNode) { '' } else { [string]$categoryNode.GetAttribute('value') }

if ($id -ne $ExpectedModuleId) {
    throw "KaiTOR Portrait Fix Module.Id is '$id', expected '$ExpectedModuleId'."
}
if ($version -ne $ExpectedVersion) {
    throw "KaiTOR Portrait Fix version is '$version', expected '$ExpectedVersion'. Server and every client must use the exact same community-module version."
}
if ($single -ne 'true' -or $multi -ne 'false' -or $category -ne 'Singleplayer') {
    throw "KaiTOR Portrait Fix must remain a Singleplayer campaign module. Found SingleplayerModule='$single', MultiplayerModule='$multi', ModuleCategory='$category'."
}

$dependencies = @($xml.SelectNodes('/Module/DependedModules/DependedModule'))
foreach ($required in @('Native', 'SandBoxCore', 'Sandbox')) {
    $matches = @($dependencies | Where-Object { [string]$_.GetAttribute('Id') -eq $required })
    if ($matches.Count -ne 1) {
        throw "KaiTOR Portrait Fix must declare '$required' exactly once."
    }
    $dependentVersion = [string]$matches[0].GetAttribute('DependentVersion')
    if ($dependentVersion -ne $ExpectedGameVersion) {
        throw "KaiTOR Portrait Fix dependency '$required' must target $ExpectedGameVersion, found '$dependentVersion'."
    }
}

$harmony = @($dependencies | Where-Object { [string]$_.GetAttribute('Id') -eq 'Bannerlord.Harmony' })
if ($harmony.Count -ne 1) {
    throw "KaiTOR Portrait Fix must declare Bannerlord.Harmony exactly once."
}
$harmonyVersion = [string]$harmony[0].GetAttribute('DependentVersion')
if ($harmonyVersion -ne $ExpectedHarmonyVersion) {
    throw "KaiTOR Portrait Fix must target Bannerlord.Harmony $ExpectedHarmonyVersion, found '$harmonyVersion'."
}

$submodules = @($xml.SelectNodes('/Module/SubModules/SubModule[DLLName/@value="KaiTORPortraitFix.dll"]'))
if ($submodules.Count -ne 1) {
    throw 'KaiTOR Portrait Fix must declare exactly one KaiTORPortraitFix.dll submodule.'
}
if (-not (Test-Path -LiteralPath $runtimeDll -PathType Leaf)) {
    throw "KaiTOR Portrait Fix runtime DLL missing: $runtimeDll"
}

if (-not (Test-Path -LiteralPath $harmonyMetadataPath -PathType Leaf)) {
    throw "KaiTOR Portrait Fix requires Bannerlord.Harmony $ExpectedHarmonyVersion, but metadata is missing: $harmonyMetadataPath"
}
try {
    [xml]$harmonyXml = Get-Content -LiteralPath $harmonyMetadataPath -Raw
}
catch {
    throw "Bannerlord.Harmony SubModule.xml is invalid: $($_.Exception.Message)"
}
$installedHarmonyId = [string]$harmonyXml.Module.Id.value
$installedHarmonyVersion = [string]$harmonyXml.Module.Version.value
if ($installedHarmonyId -ne 'Bannerlord.Harmony') {
    throw "Harmony module directory declares Id '$installedHarmonyId', expected 'Bannerlord.Harmony'."
}
if ($installedHarmonyVersion -ne $ExpectedHarmonyVersion) {
    throw "Installed Bannerlord.Harmony is '$installedHarmonyVersion', expected '$ExpectedHarmonyVersion'."
}
if (-not (Test-Path -LiteralPath $harmonyRuntimeDll -PathType Leaf)) {
    throw "Bannerlord.Harmony runtime DLL missing: $harmonyRuntimeDll"
}

$duplicateHarmony = Join-Path (Split-Path -Parent $runtimeDll) '0Harmony.dll'
if (Test-Path -LiteralPath $duplicateHarmony -PathType Leaf) {
    throw "KaiTOR Portrait Fix package contains duplicate 0Harmony.dll: $duplicateHarmony. It must reuse Bannerlord.Harmony."
}

Write-Output 'KaiTOR Portrait Fix compatibility preflight: PASS'
Write-Output "  Module:       $ExpectedModuleId $version"
Write-Output "  Runtime DLL:  $runtimeDll"
Write-Output "  Harmony:      Bannerlord.Harmony $installedHarmonyVersion"
Write-Output "  Dependencies: Native/SandBoxCore/Sandbox $ExpectedGameVersion"
Write-Output '  Coop rule:    same KaiTOR_PortraitFix version must be active on campaign server and every connecting client'
Write-Output '  Load order:   Bannerlord.Harmony -> Native/... -> TOR_Core -> KaiCleave (optional) -> KaiTOR_Stability (optional) -> KaiTOR_PortraitFix -> Coop'

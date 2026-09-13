[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BannerlordRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ExpectedModuleId = 'KaiCleave'
$ExpectedVersion = 'v0.2.1'
$ExpectedGameVersion = 'v1.3.15'
$ExpectedHarmonyVersion = 'v2.4.2.248'

$root = (Resolve-Path -LiteralPath $BannerlordRoot).Path
$moduleRoot = Join-Path $root 'Modules\KaiCleave'
$metadataPath = Join-Path $moduleRoot 'SubModule.xml'
$runtimeDll = Join-Path $moduleRoot 'bin\Win64_Shipping_Client\KaiCleave.dll'

if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
    throw "KaiCleave metadata missing: $metadataPath"
}

try {
    [xml]$xml = Get-Content -LiteralPath $metadataPath -Raw
}
catch {
    throw "KaiCleave SubModule.xml is invalid: $($_.Exception.Message)"
}

$idNode = $xml.SelectSingleNode('/Module/Id')
$versionNode = $xml.SelectSingleNode('/Module/Version')
$categoryNode = $xml.SelectSingleNode('/Module/ModuleCategory')

$id = if ($null -eq $idNode) { '' } else { [string]$idNode.GetAttribute('value') }
$version = if ($null -eq $versionNode) { '' } else { [string]$versionNode.GetAttribute('value') }
$category = if ($null -eq $categoryNode) { '' } else { [string]$categoryNode.GetAttribute('value') }

if ($id -ne $ExpectedModuleId) {
    throw "KaiCleave Module.Id is '$id', expected '$ExpectedModuleId'."
}
if ($version -ne $ExpectedVersion) {
    throw "KaiCleave version is '$version', expected '$ExpectedVersion'. Server and every client must use the exact same community-module version."
}
if ($category -ne 'Singleplayer') {
    throw "KaiCleave must remain a Singleplayer campaign module for KaiTOR's /singleplayer /server topology. Found ModuleCategory='$category'."
}

$dependencies = @($xml.SelectNodes('/Module/DependedModules/DependedModule'))
foreach ($required in @('Native', 'SandBoxCore', 'Sandbox')) {
    $matches = @($dependencies | Where-Object { [string]$_.GetAttribute('Id') -eq $required })
    if ($matches.Count -ne 1) {
        throw "KaiCleave must declare '$required' exactly once."
    }

    $dependentVersion = [string]$matches[0].GetAttribute('DependentVersion')
    if ($dependentVersion -ne $ExpectedGameVersion) {
        throw "KaiCleave dependency '$required' must target $ExpectedGameVersion, found '$dependentVersion'."
    }
}

$harmony = @($dependencies | Where-Object { [string]$_.GetAttribute('Id') -eq 'Bannerlord.Harmony' })
if ($harmony.Count -ne 1) {
    throw "KaiCleave must declare Bannerlord.Harmony exactly once."
}
$harmonyVersion = [string]$harmony[0].GetAttribute('DependentVersion')
if ($harmonyVersion -ne $ExpectedHarmonyVersion) {
    throw "KaiCleave must target Bannerlord.Harmony $ExpectedHarmonyVersion, found '$harmonyVersion'."
}

$submodules = @($xml.SelectNodes('/Module/SubModules/SubModule[DLLName/@value="KaiCleave.dll"]'))
if ($submodules.Count -ne 1) {
    throw 'KaiCleave must declare exactly one KaiCleave.dll submodule.'
}

if (-not (Test-Path -LiteralPath $runtimeDll -PathType Leaf)) {
    throw "KaiCleave runtime DLL missing: $runtimeDll"
}

$duplicateHarmony = Join-Path (Split-Path -Parent $runtimeDll) '0Harmony.dll'
if (Test-Path -LiteralPath $duplicateHarmony -PathType Leaf) {
    throw "KaiCleave package contains duplicate 0Harmony.dll: $duplicateHarmony. It must reuse Bannerlord.Harmony."
}

Write-Output 'KaiCleave compatibility preflight: PASS'
Write-Output "  Module:       $ExpectedModuleId $version"
Write-Output "  Runtime DLL:  $runtimeDll"
Write-Output "  Dependencies: Native/SandBoxCore/Sandbox $ExpectedGameVersion; Bannerlord.Harmony $ExpectedHarmonyVersion"
Write-Output '  Coop rule:    same KaiCleave version must be active on authoritative server and every connecting client'
Write-Output '  Authority:    client module is validation-only/passive; /server /coopsave process owns cleave damage logic'
Write-Output '  Load order:   TOR_Core -> KaiCleave -> KaiTOR_Stability (optional) -> Coop'

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BannerlordRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ExpectedModuleId = 'KaiTOR_Stability'
$ExpectedVersion = 'v0.5.0'
$ExpectedGameVersion = 'v1.3.15'

$root = (Resolve-Path -LiteralPath $BannerlordRoot).Path
$moduleRoot = Join-Path $root 'Modules\KaiTOR_Stability'
$metadataPath = Join-Path $moduleRoot 'SubModule.xml'
$runtimeDll = Join-Path $moduleRoot 'bin\Win64_Shipping_Client\KaiTORStability.dll'

if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
    throw "KaiTOR Stability metadata missing: $metadataPath"
}

try {
    [xml]$xml = Get-Content -LiteralPath $metadataPath -Raw
}
catch {
    throw "KaiTOR Stability SubModule.xml is invalid: $($_.Exception.Message)"
}

$idNode = $xml.SelectSingleNode('/Module/Id')
$versionNode = $xml.SelectSingleNode('/Module/Version')
$singleNode = $xml.SelectSingleNode('/Module/SingleplayerModule')
$multiNode = $xml.SelectSingleNode('/Module/MultiplayerModule')

$id = if ($null -eq $idNode) { '' } else { [string]$idNode.GetAttribute('value') }
$version = if ($null -eq $versionNode) { '' } else { [string]$versionNode.GetAttribute('value') }
$single = if ($null -eq $singleNode) { '' } else { [string]$singleNode.GetAttribute('value') }
$multi = if ($null -eq $multiNode) { '' } else { [string]$multiNode.GetAttribute('value') }

if ($id -ne $ExpectedModuleId) {
    throw "KaiTOR Stability Module.Id is '$id', expected '$ExpectedModuleId'."
}
if ($version -ne $ExpectedVersion) {
    throw "KaiTOR Stability version is '$version', expected '$ExpectedVersion'. Server and every client must use the exact same community-module version."
}
if ($single -ne 'true' -or $multi -ne 'false') {
    throw "KaiTOR Stability must be a singleplayer campaign module for the KaiTOR /singleplayer /server path. Found SingleplayerModule='$single', MultiplayerModule='$multi'."
}

$dependencies = @($xml.SelectNodes('/Module/DependedModules/DependedModule'))
foreach ($required in @('Native', 'TOR_Core')) {
    $matches = @($dependencies | Where-Object { [string]$_.GetAttribute('Id') -eq $required })
    if ($matches.Count -ne 1) {
        throw "KaiTOR Stability must declare '$required' exactly once."
    }

    $dependentVersion = [string]$matches[0].GetAttribute('DependentVersion')
    if ($dependentVersion -ne $ExpectedGameVersion) {
        throw "KaiTOR Stability dependency '$required' must target $ExpectedGameVersion, found '$dependentVersion'."
    }
}

$submodules = @($xml.SelectNodes('/Module/SubModules/SubModule[DLLName/@value="KaiTORStability.dll"]'))
if ($submodules.Count -ne 1) {
    throw 'KaiTOR Stability must declare exactly one KaiTORStability.dll submodule.'
}

if (-not (Test-Path -LiteralPath $runtimeDll -PathType Leaf)) {
    throw "KaiTOR Stability runtime DLL missing: $runtimeDll"
}

$duplicateHarmony = Join-Path (Split-Path -Parent $runtimeDll) '0Harmony.dll'
if (Test-Path -LiteralPath $duplicateHarmony -PathType Leaf) {
    throw "KaiTOR Stability package contains duplicate 0Harmony.dll: $duplicateHarmony. It must reuse the already loaded Harmony runtime."
}

Write-Output 'KaiTOR Stability compatibility preflight: PASS'
Write-Output "  Module:       $ExpectedModuleId $version"
Write-Output "  Runtime DLL:  $runtimeDll"
Write-Output "  Dependencies: Native $ExpectedGameVersion, TOR_Core $ExpectedGameVersion"
Write-Output '  Coop rule:    same KaiTOR_Stability version must be active on server and every connecting client'
Write-Output '  Load order:   TOR_Core -> KaiTOR_Stability -> Coop'

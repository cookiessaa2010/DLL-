[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BannerlordRoot,

    [string]$WorkshopRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ExpectedBannerlordVersion = '1.3.15.110062'
$ExpectedTorVersion = 'v1.3.15'
$ExpectedKaiVersion = 'v0.1.0'
$RequiredTorIds = @('TOR_Armory', 'TOR_Environment', 'TOR_Core')

function Read-ModuleIdentity {
    param([Parameter(Mandatory = $true)][string]$SubModulePath)

    if (-not (Test-Path -LiteralPath $SubModulePath -PathType Leaf)) {
        return $null
    }

    try {
        [xml]$xml = Get-Content -LiteralPath $SubModulePath -Raw
        $idNode = $xml.SelectSingleNode('/Module/Id')
        $versionNode = $xml.SelectSingleNode('/Module/Version')
        if ($null -eq $idNode) { return $null }

        return [pscustomobject]@{
            Id = [string]$idNode.GetAttribute('value')
            Version = if ($null -eq $versionNode) { '' } else { [string]$versionNode.GetAttribute('value') }
            SubModule = $SubModulePath
            Directory = Split-Path -Parent $SubModulePath
        }
    }
    catch {
        return $null
    }
}

function Resolve-WorkshopRoot {
    param([Parameter(Mandatory = $true)][string]$ResolvedBannerlordRoot)

    if (-not [string]::IsNullOrWhiteSpace($WorkshopRoot)) {
        return (Resolve-Path -LiteralPath $WorkshopRoot).Path
    }

    $common = Split-Path -Parent $ResolvedBannerlordRoot
    $steamApps = Split-Path -Parent $common
    $candidate = Join-Path $steamApps 'workshop\content\261550'
    if (Test-Path -LiteralPath $candidate -PathType Container) {
        return (Resolve-Path -LiteralPath $candidate).Path
    }

    return $null
}

function Find-TorModule {
    param(
        [Parameter(Mandatory = $true)][string]$ResolvedBannerlordRoot,
        [Parameter(Mandatory = $true)][string]$ModuleId,
        [string]$ResolvedWorkshopRoot
    )

    $manualPath = Join-Path $ResolvedBannerlordRoot ("Modules\{0}\SubModule.xml" -f $ModuleId)
    $manual = Read-ModuleIdentity -SubModulePath $manualPath
    if ($null -ne $manual -and $manual.Id -eq $ModuleId) {
        return $manual
    }

    if ([string]::IsNullOrWhiteSpace($ResolvedWorkshopRoot) -or
        -not (Test-Path -LiteralPath $ResolvedWorkshopRoot -PathType Container)) {
        return $null
    }

    $matches = @()
    foreach ($directory in Get-ChildItem -LiteralPath $ResolvedWorkshopRoot -Directory -ErrorAction Stop) {
        $identity = Read-ModuleIdentity -SubModulePath (Join-Path $directory.FullName 'SubModule.xml')
        if ($null -ne $identity -and $identity.Id -eq $ModuleId) {
            $matches += $identity
        }
    }

    if ($matches.Count -gt 1) {
        throw "Multiple Workshop modules declare Id '$ModuleId': $($matches.Directory -join '; ')"
    }

    if ($matches.Count -eq 1) { return $matches[0] }
    return $null
}

$root = (Resolve-Path -LiteralPath $BannerlordRoot).Path
$exe = Join-Path $root 'bin\Win64_Shipping_Client\Bannerlord.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw "Bannerlord.exe not found: $exe"
}

$versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
$reported = @(
    @($versionInfo.FileVersion, $versionInfo.ProductVersion) |
        Where-Object { $_ } |
        Select-Object -Unique
)
if (@($reported | Where-Object { $_ -like "$ExpectedBannerlordVersion*" }).Count -eq 0) {
    throw "Expected Bannerlord $ExpectedBannerlordVersion; found: $($reported -join ', ')"
}
Write-Output "Bannerlord runtime: PASS ($ExpectedBannerlordVersion)"

$workshop = Resolve-WorkshopRoot -ResolvedBannerlordRoot $root
foreach ($moduleId in $RequiredTorIds) {
    $module = Find-TorModule -ResolvedBannerlordRoot $root -ModuleId $moduleId -ResolvedWorkshopRoot $workshop
    if ($null -eq $module) {
        throw "Required TOR module not found: $moduleId"
    }
    if ($module.Version -ne $ExpectedTorVersion) {
        throw "$moduleId reports '$($module.Version)', expected '$ExpectedTorVersion': $($module.SubModule)"
    }
    Write-Output "$moduleId: PASS ($($module.Version))"
}

$kaiRoot = Join-Path $root 'Modules\KaiTOR_Diplomacy'
$kaiManifestPath = Join-Path $kaiRoot 'SubModule.xml'
$kai = Read-ModuleIdentity -SubModulePath $kaiManifestPath
if ($null -eq $kai -or $kai.Id -ne 'KaiTOR_Diplomacy') {
    throw "Installed KaiTOR_Diplomacy manifest not found: $kaiManifestPath"
}
if ($kai.Version -ne $ExpectedKaiVersion) {
    throw "Installed KaiTOR_Diplomacy is '$($kai.Version)', expected '$ExpectedKaiVersion'."
}

$kaiDll = Join-Path $kaiRoot 'bin\Win64_Shipping_Client\KaiTOR_Diplomacy.dll'
if (-not (Test-Path -LiteralPath $kaiDll -PathType Leaf)) {
    throw "Installed KaiTOR_Diplomacy DLL missing: $kaiDll"
}
Write-Output "KaiTOR_Diplomacy payload: PASS ($ExpectedKaiVersion)"

$contractTest = Join-Path $PSScriptRoot 'tests\Test-KaiTORDiplomacyContracts.ps1'
if (-not (Test-Path -LiteralPath $contractTest -PathType Leaf)) {
    throw "Contract test script missing: $contractTest"
}
$contractOutput = @(& $contractTest)
$contractOutput | Write-Output
if (-not ($contractOutput -match 'KaiTOR Diplomacy contract tests: PASS')) {
    throw 'KaiTOR Diplomacy contract tests did not report PASS.'
}

Write-Output 'KaiTOR Diplomacy readiness: PASS'
Write-Output 'Safe to begin the standalone TOR live-test.'

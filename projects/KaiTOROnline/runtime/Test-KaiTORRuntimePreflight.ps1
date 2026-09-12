[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BannerlordRoot,

    [string[]]$ModuleIds,

    [string]$ModuleListPath,

    [switch]$SkipVersionCheck
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ExpectedVersion = '1.3.15.110062'
$DefaultModuleList = Join-Path $PSScriptRoot 'modules.vanilla-1.3.15.txt'

function Get-ModuleIdsFromFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Module list not found: $Path"
    }

    $ids = @(
        Get-Content -LiteralPath $Path |
            ForEach-Object { $_.Trim() } |
            Where-Object { $_ -and -not $_.StartsWith('#') }
    )

    if ($ids.Count -eq 0) { throw "Module list is empty: $Path" }
    return $ids
}

function Resolve-BannerlordRoot {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $exe = Join-Path $resolved 'bin\Win64_Shipping_Client\Bannerlord.exe'
    if (Test-Path -LiteralPath $exe -PathType Leaf) {
        return [pscustomobject]@{ Root = $resolved; Exe = $exe }
    }

    $candidate = Join-Path $resolved 'Bannerlord.exe'
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        $root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $candidate))
        return [pscustomobject]@{ Root = $root; Exe = $candidate }
    }

    throw "Bannerlord.exe not found below '$Path'. Expected bin\Win64_Shipping_Client\Bannerlord.exe."
}

function Assert-ModuleMetadata {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$ModuleId
    )

    $subModulePath = Join-Path $Root ("Modules\{0}\SubModule.xml" -f $ModuleId)
    if (-not (Test-Path -LiteralPath $subModulePath -PathType Leaf)) {
        throw "Required module '$ModuleId' is missing SubModule.xml: $subModulePath"
    }

    try {
        [xml]$xml = Get-Content -LiteralPath $subModulePath -Raw
    }
    catch {
        throw "Module '$ModuleId' has invalid SubModule.xml: $($_.Exception.Message)"
    }

    if ($ModuleId -eq 'Coop') {
        # XPath is deliberate here: Windows PowerShell 5.1 + StrictMode throws a raw
        # PropertyNotFoundException when <DependedModules> is absent. Missing metadata is
        # a package-contract failure and should produce our stable diagnostic instead.
        $dependencies = @($xml.SelectNodes('/Module/DependedModules/DependedModule'))
        if ($dependencies.Count -eq 0) {
            throw "Coop SubModule.xml has no dependency metadata. This is not the KaiTOR 1.3.15 build."
        }

        $wrong = @(
            $dependencies | Where-Object {
                $dependentVersion = [string]$_.GetAttribute('DependentVersion')
                -not [string]::IsNullOrWhiteSpace($dependentVersion) -and $dependentVersion -ne 'v1.3.15'
            }
        )
        if ($wrong.Count -gt 0) {
            $details = ($wrong | ForEach-Object {
                "{0}={1}" -f $_.GetAttribute('Id'), $_.GetAttribute('DependentVersion')
            }) -join ', '
            throw "Coop dependency metadata is not locked to Bannerlord v1.3.15: $details"
        }

        $bin = Join-Path $Root 'Modules\Coop\bin\Win64_Shipping_Client'
        foreach ($name in @('Coop.dll', 'Coop.Core.dll', 'GameInterface.dll', 'Missions.dll')) {
            $managed = Join-Path $bin $name
            if (-not (Test-Path -LiteralPath $managed -PathType Leaf)) {
                throw "Coop runtime assembly missing: $managed"
            }
        }
    }

    return $subModulePath
}

$resolved = Resolve-BannerlordRoot -Path $BannerlordRoot
$root = $resolved.Root
$exe = $resolved.Exe

if (-not $SkipVersionCheck) {
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    # Windows PowerShell 5.1 unwraps a one-item pipeline result. Keep an explicit
    # array so StrictMode never turns a normal version mismatch into .Count failure.
    $reported = @(
        @($version.FileVersion, $version.ProductVersion) |
            Where-Object { $_ } |
            Select-Object -Unique
    )
    $matches = @($reported | Where-Object { $_ -like "$ExpectedVersion*" })
    if ($matches.Count -eq 0) {
        $shown = if ($reported.Count -gt 0) { $reported -join ', ' } else { '<not reported>' }
        throw "Expected Bannerlord $ExpectedVersion, but Bannerlord.exe reports: $shown"
    }
}

if (-not $ModuleIds -or @($ModuleIds).Count -eq 0) {
    $source = if ($ModuleListPath) { $ModuleListPath } else { $DefaultModuleList }
    $ModuleIds = @(Get-ModuleIdsFromFile -Path $source)
}

$seen = @{}
foreach ($moduleId in $ModuleIds) {
    if (-not $moduleId) { throw 'Module ids cannot contain blank entries.' }
    $key = $moduleId.ToLowerInvariant()
    if ($seen.ContainsKey($key)) { throw "Duplicate module id: $moduleId" }
    $seen[$key] = $true
}

if (-not ($ModuleIds -contains 'Coop')) {
    throw "The runtime module list must contain 'Coop'."
}

$validatedMetadata = [Collections.Generic.List[string]]::new()
foreach ($moduleId in $ModuleIds) {
    $validatedMetadata.Add((Assert-ModuleMetadata -Root $root -ModuleId $moduleId))
}

Write-Output 'KaiTOR Online runtime preflight: PASS'
Write-Output "  Bannerlord root: $root"
Write-Output "  Target version:  $ExpectedVersion"
Write-Output "  Modules:         $($ModuleIds -join ', ')"
Write-Output '  Coop assemblies: Coop.dll, Coop.Core.dll, GameInterface.dll, Missions.dll'
Write-Output "  Metadata files:  $($validatedMetadata.Count)"
Write-Output 'Next gate: launch the authoritative campaign process and execute the four-controller persistence/movement matrix.'

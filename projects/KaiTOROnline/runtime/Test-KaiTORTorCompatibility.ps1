[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BannerlordRoot,

    [switch]$RequireDedicatedServerMetadata
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ExpectedTorVersion = 'v1.3.15'
$RequiredDependencies = @('Native', 'SandBoxCore', 'Sandbox', 'TOR_Armory', 'TOR_Environment')
$RequiredTorRuntimeModules = @('TOR_Armory', 'TOR_Environment', 'TOR_Core')
$RequiredExactTorVersionModules = @('TOR_Armory', 'TOR_Environment', 'TOR_Core')

function Resolve-BannerlordRoot {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    if (Test-Path -LiteralPath (Join-Path $resolved 'Modules') -PathType Container) {
        return $resolved
    }

    if ((Split-Path -Leaf $resolved) -eq 'Win64_Shipping_Client') {
        # The caller may point directly at <BannerlordRoot>\bin\Win64_Shipping_Client.
        # From that directory, two parents resolve to the Bannerlord installation root.
        $candidate = Split-Path -Parent (Split-Path -Parent $resolved)
        if (Test-Path -LiteralPath (Join-Path $candidate 'Modules') -PathType Container) {
            return $candidate
        }
    }

    throw "Bannerlord Modules directory not found below '$Path'."
}

function Read-ModuleMetadata {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$ModuleId
    )

    $path = Join-Path $Root ("Modules\{0}\SubModule.xml" -f $ModuleId)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required TOR module '$ModuleId' is missing SubModule.xml: $path"
    }

    try {
        [xml]$xml = Get-Content -LiteralPath $path -Raw
    }
    catch {
        throw "TOR module '$ModuleId' has invalid SubModule.xml: $($_.Exception.Message)"
    }

    $idNode = $xml.SelectSingleNode('/Module/Id')
    $declaredId = if ($null -eq $idNode) { '' } else { [string]$idNode.GetAttribute('value') }
    if ($declaredId -ne $ModuleId) {
        throw "Module directory '$ModuleId' declares Module.Id '$declaredId'. Expected exact Id '$ModuleId'."
    }

    return [pscustomobject]@{ Path = $path; Xml = $xml }
}

function Get-ModuleVersionValue {
    param(
        [Parameter(Mandatory = $true)]$Metadata
    )

    $versionNode = $Metadata.Xml.SelectSingleNode('/Module/Version')
    if ($null -eq $versionNode) {
        return ''
    }

    return [string]$versionNode.GetAttribute('value')
}

function Assert-ExactTorModuleVersion {
    param(
        [Parameter(Mandatory = $true)][string]$ModuleId,
        [Parameter(Mandatory = $true)]$Metadata
    )

    $installedVersion = Get-ModuleVersionValue -Metadata $Metadata
    if ([string]::IsNullOrWhiteSpace($installedVersion)) {
        throw "Installed TOR module '$ModuleId' does not declare Module.Version. Exact TOR $ExpectedTorVersion is required."
    }

    if ($installedVersion -ne $ExpectedTorVersion) {
        throw "Installed TOR module '$ModuleId' is version '$installedVersion', expected '$ExpectedTorVersion'."
    }
}

function Get-TorSubModuleDllNames {
    param(
        [Parameter(Mandatory = $true)]$Metadata
    )

    # Use XPath instead of PowerShell XML property traversal. Under Windows PowerShell 5.1
    # with StrictMode, a valid content-only Bannerlord module that has no <SubModules>
    # element can otherwise raise PropertyNotFoundException before we can classify it.
    $names = [Collections.Generic.List[string]]::new()
    $subModules = @($Metadata.Xml.SelectNodes('/Module/SubModules/SubModule'))
    foreach ($subModule in $subModules) {
        $dllNode = $subModule.SelectSingleNode('DLLName')
        if ($null -eq $dllNode) {
            continue
        }

        $dllName = [string]$dllNode.GetAttribute('value')
        if (-not [string]::IsNullOrWhiteSpace($dllName)) {
            $names.Add($dllName)
        }
    }

    return $names.ToArray()
}

function Assert-TorRuntimePayload {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$ModuleId,
        [Parameter(Mandatory = $true)]$Metadata
    )

    $dllNames = @(Get-TorSubModuleDllNames -Metadata $Metadata)

    # TOR_Armory and TOR_Environment are valid asset/content modules in the real Steam
    # Workshop distribution and may not declare a managed SubModule DLL at all. TOR_Core
    # is the code-bearing module and must always declare at least one runtime DLL.
    if ($dllNames.Count -lt 1) {
        if ($ModuleId -eq 'TOR_Core') {
            throw "TOR module '$ModuleId' does not declare any runtime DLL in SubModule.xml."
        }
        return
    }

    $runtimeBin = Join-Path $Root ("Modules\{0}\bin\Win64_Shipping_Client" -f $ModuleId)
    foreach ($dllName in $dllNames) {
        if ([IO.Path]::IsPathRooted($dllName) -or [IO.Path]::GetFileName($dllName) -ne $dllName -or [IO.Path]::GetExtension($dllName) -ne '.dll') {
            throw "TOR module '$ModuleId' declares unsafe runtime DLLName '$dllName'. DLLName must be a leaf .dll file name inside its Win64_Shipping_Client directory."
        }

        $dllPath = Join-Path $runtimeBin $dllName
        if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf)) {
            throw "TOR runtime assembly missing for '$ModuleId': $dllPath"
        }
    }
}

$root = Resolve-BannerlordRoot -Path $BannerlordRoot
$core = Read-ModuleMetadata -Root $root -ModuleId 'TOR_Core'
$xml = $core.Xml

# Validate TOR_Core through the same fail-closed path used for all exact-version TOR modules.
# This intentionally handles a missing <Version> node without leaking a StrictMode property error.
Assert-ExactTorModuleVersion -ModuleId 'TOR_Core' -Metadata $core
$version = Get-ModuleVersionValue -Metadata $core

$dependencies = @($xml.SelectNodes('/Module/DependedModules/DependedModule'))
foreach ($required in $RequiredDependencies) {
    $match = @($dependencies | Where-Object { [string]$_.GetAttribute('Id') -eq $required })
    if ($match.Count -ne 1) {
        throw "TOR_Core must declare dependency '$required' exactly once."
    }

    $dependentVersion = [string]$match[0].GetAttribute('DependentVersion')
    if ($dependentVersion -ne $ExpectedTorVersion) {
        throw "TOR_Core dependency '$required' must target $ExpectedTorVersion, found '$dependentVersion'."
    }

    # Validate that the installed dependency module is present and that the directory cannot
    # masquerade as another module Id. Exact TOR module versions are enforced below; the
    # Bannerlord-native modules are version-pinned by the base runtime preflight.
    $metadata = Read-ModuleMetadata -Root $root -ModuleId $required
    if ($RequiredExactTorVersionModules -contains $required) {
        Assert-ExactTorModuleVersion -ModuleId $required -Metadata $metadata
    }
}

foreach ($runtimeModule in $RequiredTorRuntimeModules) {
    $metadata = if ($runtimeModule -eq 'TOR_Core') {
        $core
    }
    else {
        Read-ModuleMetadata -Root $root -ModuleId $runtimeModule
    }
    Assert-TorRuntimePayload -Root $root -ModuleId $runtimeModule -Metadata $metadata
}

$coreSubModule = @($xml.SelectNodes('/Module/SubModules/SubModule[DLLName/@value="TOR_Core.dll"]'))
if ($coreSubModule.Count -ne 1) {
    throw 'TOR_Core SubModule.xml must declare exactly one TOR_Core.dll submodule.'
}

$tags = @($coreSubModule[0].SelectNodes('Tags/Tag'))
$dedicatedTag = @($tags | Where-Object { [string]$_.GetAttribute('key') -eq 'DedicatedServerType' })
$dedicatedValue = if ($dedicatedTag.Count -eq 1) { [string]$dedicatedTag[0].GetAttribute('value') } else { '<missing>' }
$noRenderTag = @($tags | Where-Object { [string]$_.GetAttribute('key') -eq 'IsNoRenderModeElement' })
$noRenderValue = if ($noRenderTag.Count -eq 1) { [string]$noRenderTag[0].GetAttribute('value') } else { '<missing>' }

if ($RequireDedicatedServerMetadata -and $dedicatedValue -eq 'none') {
    throw "TOR_Core declares DedicatedServerType=none. Native dedicated-server loading is not advertised by TOR 1.3.15; use the KaiTOR Bannerlord campaign-process path and validate it at runtime before claiming TOR dedicated compatibility."
}

Write-Output 'KaiTOR Online TOR compatibility preflight: PASS'
Write-Output "  Bannerlord root:             $root"
Write-Output "  TOR_Core version:            $version"
Write-Output "  Required TOR dependencies:   $($RequiredDependencies -join ', ')"
Write-Output "  TOR runtime payload:         $($RequiredTorRuntimeModules -join ', ')"
Write-Output '  Required managed TOR DLL:    TOR_Core.dll'
Write-Output "  DedicatedServerType:         $dedicatedValue"
Write-Output "  IsNoRenderModeElement:       $noRenderValue"

if ($dedicatedValue -eq 'none') {
    Write-Warning 'TOR_Core 1.3.15 does not advertise native dedicated-server support (DedicatedServerType=none). KaiTOR must verify TOR through the Bannerlord campaign-process /server path used by Start-KaiTORCampaignServer.ps1.'
}

Write-Output 'Next TOR gate: launch the KaiTOR campaign process with TOR modules and verify the full TOR runtime stack loads before four-player snapshot/movement acceptance.'

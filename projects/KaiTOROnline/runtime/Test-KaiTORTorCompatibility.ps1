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

function Resolve-BannerlordRoot {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    if (Test-Path -LiteralPath (Join-Path $resolved 'Modules') -PathType Container) {
        return $resolved
    }

    if ((Split-Path -Leaf $resolved) -eq 'Win64_Shipping_Client') {
        $candidate = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $resolved))
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

    return [pscustomobject]@{ Path = $path; Xml = $xml }
}

$root = Resolve-BannerlordRoot -Path $BannerlordRoot
$core = Read-ModuleMetadata -Root $root -ModuleId 'TOR_Core'
$xml = $core.Xml

$id = [string]$xml.Module.Id.value
if ($id -ne 'TOR_Core') {
    throw "Expected TOR_Core module Id 'TOR_Core', found '$id'."
}

$version = [string]$xml.Module.Version.value
if ($version -ne $ExpectedTorVersion) {
    throw "Expected The Old Realms $ExpectedTorVersion, found '$version'."
}

$dependencies = @($xml.Module.DependedModules.DependedModule)
foreach ($required in $RequiredDependencies) {
    $match = @($dependencies | Where-Object { [string]$_.Id -eq $required })
    if ($match.Count -ne 1) {
        throw "TOR_Core must declare dependency '$required' exactly once."
    }

    $dependentVersion = [string]$match[0].DependentVersion
    if ($dependentVersion -ne $ExpectedTorVersion) {
        throw "TOR_Core dependency '$required' must target $ExpectedTorVersion, found '$dependentVersion'."
    }

    # Validate that the installed dependency module is present too. Its own version is checked
    # when metadata exposes a Version value; this avoids silently accepting a mixed TOR install.
    $metadata = Read-ModuleMetadata -Root $root -ModuleId $required
    $installedVersion = [string]$metadata.Xml.Module.Version.value
    if ($installedVersion -and $installedVersion -ne $ExpectedTorVersion) {
        throw "Installed TOR dependency '$required' is version '$installedVersion', expected '$ExpectedTorVersion'."
    }
}

$coreBin = Join-Path $root 'Modules\TOR_Core\bin\Win64_Shipping_Client\TOR_Core.dll'
if (-not (Test-Path -LiteralPath $coreBin -PathType Leaf)) {
    throw "TOR_Core runtime assembly missing: $coreBin"
}

$subModules = @($xml.Module.SubModules.SubModule)
$coreSubModule = @($subModules | Where-Object { [string]$_.DLLName.value -eq 'TOR_Core.dll' })
if ($coreSubModule.Count -ne 1) {
    throw 'TOR_Core SubModule.xml must declare exactly one TOR_Core.dll submodule.'
}

$tags = @($coreSubModule[0].Tags.Tag)
$dedicatedTag = @($tags | Where-Object { [string]$_.key -eq 'DedicatedServerType' })
$dedicatedValue = if ($dedicatedTag.Count -eq 1) { [string]$dedicatedTag[0].value } else { '<missing>' }
$noRenderTag = @($tags | Where-Object { [string]$_.key -eq 'IsNoRenderModeElement' })
$noRenderValue = if ($noRenderTag.Count -eq 1) { [string]$noRenderTag[0].value } else { '<missing>' }

if ($RequireDedicatedServerMetadata -and $dedicatedValue -eq 'none') {
    throw "TOR_Core declares DedicatedServerType=none. Native dedicated-server loading is not advertised by TOR 1.3.15; use the KaiTOR Bannerlord campaign-process path and validate it at runtime before claiming TOR dedicated compatibility."
}

Write-Output 'KaiTOR Online TOR compatibility preflight: PASS'
Write-Output "  Bannerlord root:             $root"
Write-Output "  TOR_Core version:            $version"
Write-Output "  Required TOR dependencies:   $($RequiredDependencies -join ', ')"
Write-Output "  TOR_Core.dll:                present"
Write-Output "  DedicatedServerType:         $dedicatedValue"
Write-Output "  IsNoRenderModeElement:       $noRenderValue"

if ($dedicatedValue -eq 'none') {
    Write-Warning 'TOR_Core 1.3.15 does not advertise native dedicated-server support (DedicatedServerType=none). KaiTOR must verify TOR through the Bannerlord campaign-process /server path used by Start-KaiTORCampaignServer.ps1.'
}

Write-Output 'Next TOR gate: launch the KaiTOR campaign process with TOR modules and verify TOR_Core loads before four-player snapshot/movement acceptance.'

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BannerlordRoot,

    [ValidateNotNullOrEmpty()]
    [string]$WorkshopRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ExpectedTorVersion = 'v1.3.15'
$RequiredTorModules = @('TOR_Armory', 'TOR_Environment', 'TOR_Core')

function Resolve-BannerlordRoot {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    if (Test-Path -LiteralPath (Join-Path $resolved 'Modules') -PathType Container) {
        return $resolved
    }

    if ((Split-Path -Leaf $resolved) -eq 'Win64_Shipping_Client') {
        $candidate = Split-Path -Parent (Split-Path -Parent $resolved)
        if (Test-Path -LiteralPath (Join-Path $candidate 'Modules') -PathType Container) {
            return $candidate
        }
    }

    throw "Bannerlord Modules directory not found below '$Path'."
}

function Resolve-WorkshopRoot {
    param(
        [Parameter(Mandatory = $true)][string]$ResolvedBannerlordRoot,
        [string]$ExplicitWorkshopRoot
    )

    if ($ExplicitWorkshopRoot) {
        $resolved = (Resolve-Path -LiteralPath $ExplicitWorkshopRoot).Path
        if (-not (Test-Path -LiteralPath $resolved -PathType Container)) {
            throw "Steam Workshop root is not a directory: $ExplicitWorkshopRoot"
        }
        return $resolved
    }

    $commonRoot = Split-Path -Parent $ResolvedBannerlordRoot
    $steamAppsRoot = Split-Path -Parent $commonRoot
    $candidate = Join-Path $steamAppsRoot 'workshop\content\261550'
    if (Test-Path -LiteralPath $candidate -PathType Container) {
        return (Resolve-Path -LiteralPath $candidate).Path
    }

    throw "Steam Workshop root was not found automatically. Pass -WorkshopRoot explicitly (expected ...\steamapps\workshop\content\261550)."
}

function Read-ModuleIdentity {
    param([Parameter(Mandatory = $true)][string]$Directory)

    $subModule = Join-Path $Directory 'SubModule.xml'
    if (-not (Test-Path -LiteralPath $subModule -PathType Leaf)) {
        return $null
    }

    try {
        [xml]$xml = Get-Content -LiteralPath $subModule -Raw
    }
    catch {
        # Workshop contains arbitrary third-party modules. A malformed unrelated module
        # must not prevent discovery of the exact TOR modules; required TOR modules still
        # fail closed below when no valid matching Id can be found.
        return $null
    }

    # XPath avoids StrictMode PropertyNotFoundException on unrelated modules that have a
    # SubModule.xml but omit <Id> or <Version> nodes.
    $idNode = $xml.SelectSingleNode('/Module/Id')
    if ($null -eq $idNode) {
        return $null
    }
    $id = [string]$idNode.GetAttribute('value')
    if ([string]::IsNullOrWhiteSpace($id)) {
        return $null
    }

    $versionNode = $xml.SelectSingleNode('/Module/Version')
    $version = if ($null -eq $versionNode) { '' } else { [string]$versionNode.GetAttribute('value') }

    return [pscustomobject]@{
        Id = $id
        Version = $version
        Directory = $Directory
        SubModule = $subModule
    }
}

function Find-WorkshopModule {
    param(
        [Parameter(Mandatory = $true)][string]$ResolvedWorkshopRoot,
        [Parameter(Mandatory = $true)][string]$ModuleId
    )

    $matches = [Collections.Generic.List[object]]::new()
    foreach ($directory in Get-ChildItem -LiteralPath $ResolvedWorkshopRoot -Directory -ErrorAction Stop) {
        $identity = Read-ModuleIdentity -Directory $directory.FullName
        if ($null -ne $identity -and $identity.Id -eq $ModuleId) {
            $matches.Add($identity)
        }
    }

    if ($matches.Count -eq 0) {
        throw "Required Steam Workshop TOR module '$ModuleId' was not found below '$ResolvedWorkshopRoot'."
    }
    if ($matches.Count -gt 1) {
        $paths = ($matches | ForEach-Object { $_.Directory }) -join '; '
        throw "Multiple Steam Workshop modules declare Id '$ModuleId': $paths"
    }

    $match = $matches[0]
    if ([string]::IsNullOrWhiteSpace($match.Version)) {
        throw "Steam Workshop TOR module '$ModuleId' does not declare Module.Version: $($match.SubModule)"
    }
    if ($match.Version -ne $ExpectedTorVersion) {
        throw "Steam Workshop TOR module '$ModuleId' is version '$($match.Version)', expected '$ExpectedTorVersion'."
    }

    return $match
}

function Assert-ExistingTargetIsSafe {
    param(
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [Parameter(Mandatory = $true)][string]$ModuleId
    )

    $item = Get-Item -LiteralPath $TargetPath -Force
    if (-not $item.PSIsContainer) {
        throw "Refusing to replace non-directory path: $TargetPath"
    }
    if (-not (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq [IO.FileAttributes]::ReparsePoint)) {
        throw "Refusing to replace existing real module directory '$TargetPath'. Remove or relocate it manually if Workshop linking is desired."
    }

    $identity = Read-ModuleIdentity -Directory $TargetPath
    if ($null -eq $identity -or $identity.Id -ne $ModuleId -or $identity.Version -ne $ExpectedTorVersion) {
        throw "Existing junction '$TargetPath' does not expose expected $ModuleId $ExpectedTorVersion. Refusing to modify it automatically."
    }
}

$root = Resolve-BannerlordRoot -Path $BannerlordRoot
$workshop = Resolve-WorkshopRoot -ResolvedBannerlordRoot $root -ExplicitWorkshopRoot $WorkshopRoot
$modulesRoot = Join-Path $root 'Modules'

$resolvedModules = [Collections.Generic.List[object]]::new()
foreach ($moduleId in $RequiredTorModules) {
    $source = Find-WorkshopModule -ResolvedWorkshopRoot $workshop -ModuleId $moduleId
    $target = Join-Path $modulesRoot $moduleId

    if (Test-Path -LiteralPath $target) {
        Assert-ExistingTargetIsSafe -TargetPath $target -ModuleId $moduleId
        Write-Output "Workshop junction already ready: $moduleId -> $($source.Directory)"
    }
    else {
        if ($PSCmdlet.ShouldProcess($target, "Create junction to $($source.Directory)")) {
            New-Item -ItemType Junction -Path $target -Target $source.Directory | Out-Null
            Write-Output "Created Workshop junction: $moduleId -> $($source.Directory)"
        }
    }

    $resolvedModules.Add([pscustomobject]@{
        ModuleId = $moduleId
        Version = $source.Version
        WorkshopDirectory = $source.Directory
        BannerlordDirectory = $target
    })
}

Write-Output 'KaiTOR TOR Workshop link preparation: PASS'
Write-Output "  Bannerlord root: $root"
Write-Output "  Workshop root:  $workshop"
foreach ($module in $resolvedModules) {
    Write-Output "  $($module.ModuleId): $($module.Version) -> $($module.WorkshopDirectory)"
}
Write-Output 'No TOR files were copied. Bannerlord Modules contains only NTFS junctions to the Steam Workshop payload.'

# Shared TOR Steam Workshop discovery and temporary-staging helpers.
# Keep this file ASCII-only so Windows PowerShell 5.1 can parse it without BOM assumptions.

$script:KaiTORTorExpectedVersion = 'v1.3.15'
$script:KaiTORTorRequiredModuleIds = @('TOR_Armory', 'TOR_Environment', 'TOR_Core')

function Resolve-KaiTORBannerlordRoot {
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

function Resolve-KaiTORWorkshopRoot {
    param(
        [Parameter(Mandatory = $true)][string]$ResolvedBannerlordRoot,
        [string]$ExplicitWorkshopRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitWorkshopRoot)) {
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

function Read-KaiTORTorModuleIdentity {
    param([Parameter(Mandatory = $true)][string]$Directory)

    $subModule = Join-Path $Directory 'SubModule.xml'
    if (-not (Test-Path -LiteralPath $subModule -PathType Leaf)) {
        return $null
    }

    try {
        [xml]$xml = Get-Content -LiteralPath $subModule -Raw
    }
    catch {
        return $null
    }

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

function Resolve-KaiTORTorWorkshopModules {
    param(
        [Parameter(Mandatory = $true)][string]$BannerlordRoot,
        [string]$WorkshopRoot
    )

    $root = Resolve-KaiTORBannerlordRoot -Path $BannerlordRoot
    $workshop = Resolve-KaiTORWorkshopRoot -ResolvedBannerlordRoot $root -ExplicitWorkshopRoot $WorkshopRoot
    $all = [Collections.Generic.List[object]]::new()

    foreach ($directory in Get-ChildItem -LiteralPath $workshop -Directory -ErrorAction Stop) {
        $identity = Read-KaiTORTorModuleIdentity -Directory $directory.FullName
        if ($null -ne $identity) {
            $all.Add($identity)
        }
    }

    $resolvedModules = [Collections.Generic.List[object]]::new()
    foreach ($moduleId in $script:KaiTORTorRequiredModuleIds) {
        $matches = @($all | Where-Object { $_.Id -eq $moduleId })
        if ($matches.Count -eq 0) {
            throw "Required Steam Workshop TOR module '$moduleId' was not found below '$workshop'."
        }
        if ($matches.Count -gt 1) {
            $paths = ($matches | ForEach-Object { $_.Directory }) -join '; '
            throw "Multiple Steam Workshop modules declare Id '$moduleId': $paths"
        }

        $match = $matches[0]
        if ([string]::IsNullOrWhiteSpace($match.Version)) {
            throw "Steam Workshop TOR module '$moduleId' does not declare Module.Version: $($match.SubModule)"
        }
        if ($match.Version -ne $script:KaiTORTorExpectedVersion) {
            throw "Steam Workshop TOR module '$moduleId' is version '$($match.Version)', expected '$($script:KaiTORTorExpectedVersion)'."
        }

        $resolvedModules.Add($match)
    }

    return $resolvedModules.ToArray()
}

function New-KaiTORTorEphemeralWorkshopLinks {
    param(
        [Parameter(Mandatory = $true)][string]$BannerlordRoot,
        [Parameter(Mandatory = $true)][object[]]$ResolvedModules
    )

    $root = Resolve-KaiTORBannerlordRoot -Path $BannerlordRoot
    $modulesRoot = Join-Path $root 'Modules'
    $created = [Collections.Generic.List[string]]::new()

    foreach ($module in $ResolvedModules) {
        $target = Join-Path $modulesRoot $module.Id
        if (Test-Path -LiteralPath $target) {
            $item = Get-Item -LiteralPath $target -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq [IO.FileAttributes]::ReparsePoint) {
                throw "Persistent TOR reparse point already exists: $target. Remove the stale junction before KaiTOR launch so normal Steam startup remains clean."
            }
            throw "TOR module path already exists as a real directory: $target. Refusing to mix a manual TOR install with Workshop staging."
        }

        New-Item -ItemType Junction -Path $target -Target $module.Directory | Out-Null
        $created.Add($target)
    }

    return $created.ToArray()
}

function Remove-KaiTORTorEphemeralWorkshopLinks {
    param([string[]]$Paths)

    $items = @($Paths)
    for ($i = $items.Count - 1; $i -ge 0; $i--) {
        $path = $items[$i]
        if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path)) {
            continue
        }

        $item = Get-Item -LiteralPath $path -Force
        if (-not (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq [IO.FileAttributes]::ReparsePoint)) {
            Write-Warning "Refusing cleanup because staged TOR path is no longer a reparse point: $path"
            continue
        }

        & $env:ComSpec /d /c "rmdir `"$path`"" | Out-Null
        if ($LASTEXITCODE -ne 0 -and (Test-Path -LiteralPath $path)) {
            Write-Warning "Failed to remove temporary TOR junction: $path"
        }
    }
}

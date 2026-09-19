[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BannerlordRoot,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$SaveName,

    [ValidateSet('public', 'friends_only', 'none')]
    [string]$Visibility = 'none',

    [ValidateLength(0, 128)]
    [string]$Password = '',

    [ValidateNotNullOrEmpty()]
    [string]$WorkshopRoot,

    [switch]$ManagedMode,

    [switch]$SkipVersionCheck,

    [switch]$DryRun,

    [switch]$Wait
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$TorPreflight = Join-Path $PSScriptRoot 'Test-KaiTORTorCompatibility.ps1'
$CleavePreflight = Join-Path $PSScriptRoot 'Test-KaiTORCleaveCompatibility.ps1'
$StabilityPreflight = Join-Path $PSScriptRoot 'Test-KaiTORStabilityCompatibility.ps1'
$PortraitPreflight = Join-Path $PSScriptRoot 'Test-KaiTORPortraitFixCompatibility.ps1'
$CampaignLauncher = Join-Path $PSScriptRoot 'Start-KaiTORCampaignServer.ps1'
$TorModuleList = Join-Path $PSScriptRoot 'modules.tor-1.3.15.txt'
$WorkshopRuntime = Join-Path $PSScriptRoot 'KaiTORTorWorkshopRuntime.ps1'

foreach ($required in @(
    $TorPreflight,
    $CleavePreflight,
    $StabilityPreflight,
    $PortraitPreflight,
    $CampaignLauncher,
    $TorModuleList,
    $WorkshopRuntime
)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "KaiTOR TOR launch dependency missing: $required"
    }
}

. $WorkshopRuntime
$root = Resolve-KaiTORBannerlordRoot -Path $BannerlordRoot
$requiredTor = @('TOR_Armory', 'TOR_Environment', 'TOR_Core')

# Persistent TOR links in the real Bannerlord Modules directory can interfere with the
# normal Steam/TaleWorlds launcher. KaiTOR only permits real manual TOR directories or
# links created inside this invocation and removed in finally.
foreach ($moduleId in $requiredTor) {
    $moduleDirectory = Join-Path $root ("Modules\{0}" -f $moduleId)
    if (Test-Path -LiteralPath $moduleDirectory) {
        $item = Get-Item -LiteralPath $moduleDirectory -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq [IO.FileAttributes]::ReparsePoint) {
            throw "Persistent TOR reparse point detected: $moduleDirectory. Remove it before KaiTOR launch; Workshop TOR is staged temporarily by this script."
        }
    }
}

$presentTor = @($requiredTor | Where-Object {
    Test-Path -LiteralPath (Join-Path $root ("Modules\{0}\SubModule.xml" -f $_)) -PathType Leaf
})
$missingTor = @($requiredTor | Where-Object { $presentTor -notcontains $_ })

if ($presentTor.Count -gt 0 -and $missingTor.Count -gt 0) {
    throw "Mixed TOR layout detected under Bannerlord Modules. Present: $($presentTor -join ', '); missing: $($missingTor -join ', '). Refusing to combine a partial manual install with Workshop staging."
}

$createdLinks = [Collections.Generic.List[string]]::new()
$usingTorWorkshopStage = $missingTor.Count -eq $requiredTor.Count
$resolvedWorkshopRoot = $null

try {
    if ($usingTorWorkshopStage) {
        $runningGame = @(Get-Process -Name 'Bannerlord','TaleWorlds.MountAndBlade.Launcher' -ErrorAction SilentlyContinue)
        if ($runningGame.Count -gt 0) {
            throw 'Bannerlord or the TaleWorlds launcher is already running. Close it before KaiTOR creates temporary Workshop staging links.'
        }

        $resolvedWorkshopModules = @(Resolve-KaiTORTorWorkshopModules -BannerlordRoot $root -WorkshopRoot $WorkshopRoot)
        foreach ($link in @(New-KaiTORTorEphemeralWorkshopLinks -BannerlordRoot $root -ResolvedModules $resolvedWorkshopModules)) {
            $createdLinks.Add($link)
        }
        Write-Output 'TOR Workshop staging: ACTIVE (temporary junctions created for this KaiTOR invocation only).'
        foreach ($module in $resolvedWorkshopModules) {
            Write-Output "  $($module.Id): $($module.Version) -> $($module.Directory)"
        }
    }

    # Resolve the Workshop root even when TOR itself is installed manually. This lets the
    # Steam versions of our optional Kai modules participate in direct Bannerlord.exe /server
    # launches without persistent links.
    try {
        $resolvedWorkshopRoot = Resolve-KaiTORWorkshopRoot -ResolvedBannerlordRoot $root -ExplicitWorkshopRoot $WorkshopRoot
    }
    catch {
        if (-not [string]::IsNullOrWhiteSpace($WorkshopRoot)) {
            throw
        }
        $resolvedWorkshopRoot = $null
    }

    $optionalSpecs = @(
        [pscustomobject]@{ Id = 'KaiCleave'; Version = 'v1.3.15.31' },
        [pscustomobject]@{ Id = 'KaiTOR_Stability'; Version = 'v1.3.15.50' },
        [pscustomobject]@{ Id = 'KaiTOR_PortraitFix'; Version = 'v1.3.15.60' }
    )

    foreach ($spec in $optionalSpecs) {
        $metadata = Join-Path $root ("Modules\{0}\SubModule.xml" -f $spec.Id)
        if (Test-Path -LiteralPath $metadata -PathType Leaf) {
            continue
        }
        if ([string]::IsNullOrWhiteSpace($resolvedWorkshopRoot)) {
            continue
        }

        $workshopModule = Find-KaiTORWorkshopModule -BannerlordRoot $root -WorkshopRoot $resolvedWorkshopRoot -ModuleId $spec.Id -ExpectedVersion $spec.Version
        if ($null -ne $workshopModule) {
            foreach ($link in @(New-KaiTOREphemeralWorkshopLinks -BannerlordRoot $root -ResolvedModules @($workshopModule))) {
                $createdLinks.Add($link)
            }
            Write-Output "Kai Workshop staging: $($workshopModule.Id) $($workshopModule.Version) -> $($workshopModule.Directory)"
        }
    }

    $cleaveMetadata = Join-Path $root 'Modules\KaiCleave\SubModule.xml'
    $stabilityMetadata = Join-Path $root 'Modules\KaiTOR_Stability\SubModule.xml'
    $portraitMetadata = Join-Path $root 'Modules\KaiTOR_PortraitFix\SubModule.xml'
    $cleaveInstalled = Test-Path -LiteralPath $cleaveMetadata -PathType Leaf
    $stabilityInstalled = Test-Path -LiteralPath $stabilityMetadata -PathType Leaf
    $portraitInstalled = Test-Path -LiteralPath $portraitMetadata -PathType Leaf
    $needsHarmony = $cleaveInstalled -or $portraitInstalled

    # Cleave and Portrait Fix both depend on Bannerlord.Harmony. Stage the exact supported
    # Workshop Harmony build when it is not already available under Bannerlord\Modules.
    if ($needsHarmony) {
        $harmonyMetadata = Join-Path $root 'Modules\Bannerlord.Harmony\SubModule.xml'
        if (-not (Test-Path -LiteralPath $harmonyMetadata -PathType Leaf)) {
            if ([string]::IsNullOrWhiteSpace($resolvedWorkshopRoot)) {
                throw 'KaiCleave/KaiTOR_PortraitFix requires Bannerlord.Harmony v2.4.2.248, but Harmony is not installed under Modules and no Steam Workshop root is available.'
            }

            $harmonyWorkshop = Find-KaiTORWorkshopModule -BannerlordRoot $root -WorkshopRoot $resolvedWorkshopRoot -ModuleId 'Bannerlord.Harmony' -ExpectedVersion 'v2.4.2.248'
            if ($null -eq $harmonyWorkshop) {
                throw 'KaiCleave/KaiTOR_PortraitFix requires Bannerlord.Harmony v2.4.2.248, but that Workshop module was not found.'
            }

            foreach ($link in @(New-KaiTOREphemeralWorkshopLinks -BannerlordRoot $root -ResolvedModules @($harmonyWorkshop))) {
                $createdLinks.Add($link)
            }
            Write-Output "Kai Workshop staging: $($harmonyWorkshop.Id) $($harmonyWorkshop.Version) -> $($harmonyWorkshop.Directory)"
        }
    }

    $torOutput = @(& $TorPreflight -BannerlordRoot $root 3>&1)
    $torText = $torOutput | Out-String
    Write-Output $torText.TrimEnd()
    if ($torText -notmatch 'KaiTOR Online TOR compatibility preflight: PASS') {
        throw 'TOR compatibility preflight did not report PASS; refusing to launch the TOR campaign process.'
    }
    Write-Output 'TOR compatibility preflight: PASS (TOR launch authorized).'

    $baseModuleIds = @(
        Get-Content -LiteralPath $TorModuleList |
            ForEach-Object { $_.Trim() } |
            Where-Object { $_ -and -not $_.StartsWith('#') }
    )

    if ($cleaveInstalled) {
        $cleaveOutput = @(& $CleavePreflight -BannerlordRoot $root 3>&1)
        $cleaveText = $cleaveOutput | Out-String
        Write-Output $cleaveText.TrimEnd()
        if ($cleaveText -notmatch 'KaiCleave compatibility preflight: PASS') {
            throw 'KaiCleave compatibility preflight did not report PASS; refusing to launch a mixed Coop/Cleave runtime.'
        }
        Write-Output 'KaiCleave integration: ACTIVE (v1.3.15.31).'
    }
    else {
        Write-Output 'KaiCleave integration: INACTIVE.'
    }

    if ($stabilityInstalled) {
        $stabilityOutput = @(& $StabilityPreflight -BannerlordRoot $root 3>&1)
        $stabilityText = $stabilityOutput | Out-String
        Write-Output $stabilityText.TrimEnd()
        if ($stabilityText -notmatch 'KaiTOR Stability compatibility preflight: PASS') {
            throw 'KaiTOR Stability compatibility preflight did not report PASS; refusing to launch a mixed Coop/Stability runtime.'
        }
        Write-Output 'KaiTOR Stability integration: ACTIVE (v1.3.15.50).'
    }
    else {
        Write-Output 'KaiTOR Stability integration: INACTIVE.'
    }

    if ($portraitInstalled) {
        $portraitOutput = @(& $PortraitPreflight -BannerlordRoot $root 3>&1)
        $portraitText = $portraitOutput | Out-String
        Write-Output $portraitText.TrimEnd()
        if ($portraitText -notmatch 'KaiTOR Portrait Fix compatibility preflight: PASS') {
            throw 'KaiTOR Portrait Fix compatibility preflight did not report PASS; refusing to launch a mixed Coop/Portrait Fix runtime.'
        }
        Write-Output 'KaiTOR Portrait Fix integration: ACTIVE (v1.3.15.60).'
    }
    else {
        Write-Output 'KaiTOR Portrait Fix integration: INACTIVE.'
    }

    $finalModuleIds = [Collections.Generic.List[string]]::new()

    # Cleave and Portrait Fix explicitly depend on Harmony. Because KaiTOR constructs the
    # _MODULES_ token itself, Harmony must be explicitly first whenever either is active.
    if ($needsHarmony) {
        $finalModuleIds.Add('Bannerlord.Harmony')
    }

    foreach ($moduleId in $baseModuleIds) {
        if ($moduleId -eq 'Coop') {
            if ($cleaveInstalled) {
                $finalModuleIds.Add('KaiCleave')
            }
            if ($stabilityInstalled) {
                $finalModuleIds.Add('KaiTOR_Stability')
            }
            if ($portraitInstalled) {
                $finalModuleIds.Add('KaiTOR_PortraitFix')
            }
        }
        $finalModuleIds.Add($moduleId)
    }

    if (-not $finalModuleIds.Contains('Coop')) {
        throw "TOR module list does not contain required 'Coop' module."
    }
    if ($cleaveInstalled -and -not $finalModuleIds.Contains('KaiCleave')) {
        throw 'KaiCleave is installed but could not be inserted into the campaign-process module order.'
    }
    if ($stabilityInstalled -and -not $finalModuleIds.Contains('KaiTOR_Stability')) {
        throw 'KaiTOR Stability is installed but could not be inserted into the campaign-process module order.'
    }
    if ($portraitInstalled -and -not $finalModuleIds.Contains('KaiTOR_PortraitFix')) {
        throw 'KaiTOR Portrait Fix is installed but could not be inserted into the campaign-process module order.'
    }
    if ($needsHarmony -and -not $finalModuleIds.Contains('Bannerlord.Harmony')) {
        throw 'Kai optional modules require Bannerlord.Harmony, but it could not be inserted into the campaign-process module order.'
    }

    $usingAnyWorkshopStage = @($createdLinks).Count -gt 0
    $launcherArgs = @{
        BannerlordRoot = $root
        SaveName = $SaveName
        ModuleIds = $finalModuleIds.ToArray()
        Visibility = $Visibility
        Password = $Password
        ManagedMode = $ManagedMode
        SkipVersionCheck = $SkipVersionCheck
        DryRun = $DryRun
        Wait = ($Wait -or $usingAnyWorkshopStage)
        NoExitOnWait = $usingAnyWorkshopStage
    }

    if ($usingAnyWorkshopStage -and -not $DryRun) {
        Write-Output 'Workshop staging requires the launcher to wait for the server process so all temporary junctions can be removed after Bannerlord exits.'
    }

    & $CampaignLauncher @launcherArgs
}
finally {
    if (@($createdLinks).Count -gt 0) {
        Remove-KaiTORTorEphemeralWorkshopLinks -Paths $createdLinks.ToArray()
        Write-Output 'Workshop staging cleanup: PASS (temporary TOR/Kai junctions removed).'
    }
}

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
$CampaignLauncher = Join-Path $PSScriptRoot 'Start-KaiTORCampaignServer.ps1'
$TorModuleList = Join-Path $PSScriptRoot 'modules.tor-1.3.15.txt'
$WorkshopRuntime = Join-Path $PSScriptRoot 'KaiTORTorWorkshopRuntime.ps1'

foreach ($required in @($TorPreflight, $CampaignLauncher, $TorModuleList, $WorkshopRuntime)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "KaiTOR TOR launch dependency missing: $required"
    }
}

. $WorkshopRuntime
$root = Resolve-KaiTORBannerlordRoot -Path $BannerlordRoot
$requiredTor = @('TOR_Armory', 'TOR_Environment', 'TOR_Core')
$presentTor = @($requiredTor | Where-Object {
    Test-Path -LiteralPath (Join-Path $root ("Modules\{0}\SubModule.xml" -f $_)) -PathType Leaf
})
$missingTor = @($requiredTor | Where-Object { $presentTor -notcontains $_ })

if ($presentTor.Count -gt 0 -and $missingTor.Count -gt 0) {
    throw "Mixed TOR layout detected under Bannerlord Modules. Present: $($presentTor -join ', '); missing: $($missingTor -join ', '). Refusing to combine a partial manual install with Workshop staging."
}

$createdLinks = @()
$usingWorkshopStage = $missingTor.Count -eq $requiredTor.Count

if ($usingWorkshopStage) {
    $runningGame = @(Get-Process -Name 'Bannerlord','TaleWorlds.MountAndBlade.Launcher' -ErrorAction SilentlyContinue)
    if ($runningGame.Count -gt 0) {
        throw 'Bannerlord or the TaleWorlds launcher is already running. Close it before KaiTOR creates temporary TOR Workshop staging links.'
    }

    $resolvedWorkshopModules = @(Resolve-KaiTORTorWorkshopModules -BannerlordRoot $root -WorkshopRoot $WorkshopRoot)
    $createdLinks = @(New-KaiTORTorEphemeralWorkshopLinks -BannerlordRoot $root -ResolvedModules $resolvedWorkshopModules)
    Write-Output 'TOR Workshop staging: ACTIVE (temporary junctions created for this KaiTOR invocation only).'
    foreach ($module in $resolvedWorkshopModules) {
        Write-Output "  $($module.Id): $($module.Version) -> $($module.Directory)"
    }
}

try {
    $torOutput = @(& $TorPreflight -BannerlordRoot $root 3>&1)
    $torText = $torOutput | Out-String
    Write-Output $torText.TrimEnd()
    if ($torText -notmatch 'KaiTOR Online TOR compatibility preflight: PASS') {
        throw 'TOR compatibility preflight did not report PASS; refusing to launch the TOR campaign process.'
    }
    Write-Output 'TOR compatibility preflight: PASS (TOR launch authorized).'

    $launcherArgs = @{
        BannerlordRoot = $root
        SaveName = $SaveName
        ModuleListPath = $TorModuleList
        Visibility = $Visibility
        Password = $Password
        ManagedMode = $ManagedMode
        SkipVersionCheck = $SkipVersionCheck
        DryRun = $DryRun
        Wait = ($Wait -or $usingWorkshopStage)
        NoExitOnWait = $usingWorkshopStage
    }

    if ($usingWorkshopStage -and -not $DryRun) {
        Write-Output 'TOR Workshop staging requires the launcher to wait for the server process so cleanup can run after Bannerlord exits.'
    }

    & $CampaignLauncher @launcherArgs
}
finally {
    if (@($createdLinks).Count -gt 0) {
        Remove-KaiTORTorEphemeralWorkshopLinks -Paths $createdLinks
        Write-Output 'TOR Workshop staging cleanup: PASS (temporary junctions removed).'
    }
}

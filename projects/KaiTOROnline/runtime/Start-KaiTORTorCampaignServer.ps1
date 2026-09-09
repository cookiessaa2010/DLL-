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

foreach ($required in @($TorPreflight, $CampaignLauncher, $TorModuleList)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "KaiTOR TOR launch dependency missing: $required"
    }
}

$torOutput = @(& $TorPreflight -BannerlordRoot $BannerlordRoot 3>&1)
$torText = $torOutput | Out-String
Write-Output $torText.TrimEnd()
if ($torText -notmatch 'KaiTOR Online TOR compatibility preflight: PASS') {
    throw 'TOR compatibility preflight did not report PASS; refusing to launch the TOR campaign process.'
}
Write-Output 'TOR compatibility preflight: PASS (TOR launch authorized).'

$launcherArgs = @{
    BannerlordRoot = $BannerlordRoot
    SaveName = $SaveName
    ModuleListPath = $TorModuleList
    Visibility = $Visibility
    Password = $Password
    ManagedMode = $ManagedMode
    SkipVersionCheck = $SkipVersionCheck
    DryRun = $DryRun
    Wait = $Wait
}

& $CampaignLauncher @launcherArgs

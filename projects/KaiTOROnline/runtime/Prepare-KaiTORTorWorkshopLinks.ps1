[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BannerlordRoot,

    [ValidateNotNullOrEmpty()]
    [string]$WorkshopRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$WorkshopRuntime = Join-Path $PSScriptRoot 'KaiTORTorWorkshopRuntime.ps1'
if (-not (Test-Path -LiteralPath $WorkshopRuntime -PathType Leaf)) {
    throw "KaiTOR TOR Workshop runtime helper is missing: $WorkshopRuntime"
}
. $WorkshopRuntime

$root = Resolve-KaiTORBannerlordRoot -Path $BannerlordRoot
$workshop = Resolve-KaiTORWorkshopRoot -ResolvedBannerlordRoot $root -ExplicitWorkshopRoot $WorkshopRoot
$resolvedModules = @(Resolve-KaiTORTorWorkshopModules -BannerlordRoot $root -WorkshopRoot $workshop)

Write-Output 'KaiTOR TOR Workshop discovery: PASS'
Write-Output "  Bannerlord root: $root"
Write-Output "  Workshop root:  $workshop"
foreach ($module in $resolvedModules) {
    Write-Output "  $($module.Id): $($module.Version) -> $($module.Directory)"
}
Write-Warning 'Persistent TOR junction creation is disabled because it can interfere with normal Bannerlord Steam startup.'
Write-Output 'No Bannerlord Modules paths were modified. Start-KaiTORTorCampaignServer.ps1 creates temporary TOR junctions only for the KaiTOR server session and removes them afterward.'

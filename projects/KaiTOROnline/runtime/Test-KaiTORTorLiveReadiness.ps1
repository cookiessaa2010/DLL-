[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$PackageRoot,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BannerlordRoot,

    [ValidateNotNullOrEmpty()]
    [string]$SaveName = 'KaiTOR-Live-Test',

    [ValidateNotNullOrEmpty()]
    [string]$WorkshopRoot,

    [switch]$SkipVersionCheck
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$package = (Resolve-Path -LiteralPath $PackageRoot).Path
$bannerlord = (Resolve-Path -LiteralPath $BannerlordRoot).Path

$packageValidator = Join-Path $package 'Runtime/Test-KaiTORTorTestPackage.ps1'
$torLauncher = Join-Path $package 'Runtime/Start-KaiTORTorCampaignServer.ps1'

foreach ($required in @($packageValidator, $torLauncher)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "KaiTOR live-readiness dependency missing: $required"
    }
}

& $packageValidator -PackageRoot $package | Out-Host

$relativePayload = @(
    'Modules/Coop/SubModule.xml',
    'Modules/Coop/bin/Win64_Shipping_Client/Coop.dll',
    'Modules/Coop/bin/Win64_Shipping_Client/Coop.Core.dll',
    'Modules/Coop/bin/Win64_Shipping_Client/GameInterface.dll',
    'Modules/Coop/bin/Win64_Shipping_Client/Missions.dll'
)

foreach ($relative in $relativePayload) {
    $source = Join-Path $package $relative
    $installed = Join-Path $bannerlord $relative

    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "KaiTOR package payload missing: $relative"
    }
    if (-not (Test-Path -LiteralPath $installed -PathType Leaf)) {
        throw "KaiTOR payload is not installed in Bannerlord: $relative"
    }

    $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    $installedHash = (Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash
    if ($sourceHash -ne $installedHash) {
        throw "Installed KaiTOR payload does not match the validated package: $relative"
    }
}

$launcherArgs = @{
    BannerlordRoot = $bannerlord
    SaveName = $SaveName
    DryRun = $true
}
if ($SkipVersionCheck) {
    $launcherArgs.SkipVersionCheck = $true
}
if (-not [string]::IsNullOrWhiteSpace($WorkshopRoot)) {
    $launcherArgs.WorkshopRoot = $WorkshopRoot
}

# The TOR wrapper owns Workshop staging. This keeps normal Bannerlord Modules clean and
# lets the same dry-run validate both compatibility and the base campaign launch contract.
$launchOutput = & $torLauncher @launcherArgs 3>&1 2>&1 | Out-String
Write-Output $launchOutput.TrimEnd()

if ($launchOutput -notmatch 'TOR compatibility preflight: PASS \(TOR launch authorized\)') {
    throw 'TOR launcher did not pass the compatibility gate during live-readiness dry run.'
}
if ($launchOutput -notmatch 'Runtime preflight: PASS \(launch authorized\)') {
    throw 'TOR launcher did not pass the runtime preflight during live-readiness dry run.'
}
if ($launchOutput -notmatch 'DRY RUN: process not started') {
    throw 'TOR launcher dry run did not complete; refusing to mark the installation ready.'
}
if ($launchOutput -match 'TOR Workshop staging: ACTIVE' -and $launchOutput -notmatch 'TOR Workshop staging cleanup: PASS') {
    throw 'TOR Workshop dry-run staging did not report cleanup; refusing to mark the installation ready.'
}

Write-Output 'KaiTOR TOR live readiness: PASS'
Write-Output "  Package root:       $package"
Write-Output "  Bannerlord root:    $bannerlord"
Write-Output '  Installed payload:  exact SHA-256 match to validated package'
Write-Output '  TOR compatibility:  PASS'
Write-Output '  Launch preflight:   PASS'
Write-Output '  Admission contract: 4 simultaneous players'
Write-Output '  Workshop policy:    no persistent TOR junctions in Bannerlord Modules'
Write-Output 'Next gate: start the real authoritative campaign process, connect clients, and collect live runtime/snapshot evidence.'

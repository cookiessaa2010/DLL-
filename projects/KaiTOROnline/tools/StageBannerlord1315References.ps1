param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot,

    [string]$Version = '1.3.15.110062'
)

$ErrorActionPreference = 'Stop'

$nugetRoot = Join-Path $HOME '.nuget/packages'
$packagePattern = Join-Path $nugetRoot 'bannerlord.referenceassemblies.*'
$packageRoots = Get-ChildItem $packagePattern -Directory -ErrorAction Stop

$referenceFiles = foreach ($packageRoot in $packageRoots) {
    $refDir = Join-Path $packageRoot.FullName "$Version/ref/net472"
    if (Test-Path $refDir) {
        Get-ChildItem $refDir -File | Where-Object { $_.Extension -in '.dll', '.exe' }
    }
}

if (-not $referenceFiles -or $referenceFiles.Count -eq 0) {
    throw "No Bannerlord reference assemblies found for $Version under $nugetRoot. Restore Bannerlord.ReferenceAssemblies first."
}

$destinations = @(
    'mb2/bin/Win64_Shipping_Client',
    'mb2/Modules/Native/bin/Win64_Shipping_Client',
    'mb2/Modules/SandBox/bin/Win64_Shipping_Client',
    'mb2/Modules/SandBoxCore/bin/Win64_Shipping_Client',
    'mb2/Modules/StoryMode/bin/Win64_Shipping_Client',
    'mb2/Modules/CustomBattle/bin/Win64_Shipping_Client',
    'mb2/Modules/Multiplayer/bin/Win64_Shipping_Client',
    'mb2/Modules/BirthAndDeath/bin/Win64_Shipping_Client',
    'mb2/Modules/NavalDLC/bin/Win64_Shipping_Client'
)

foreach ($relativeDestination in $destinations) {
    $destination = Join-Path $UpstreamRoot $relativeDestination
    New-Item -ItemType Directory -Path $destination -Force | Out-Null

    foreach ($file in $referenceFiles) {
        Copy-Item $file.FullName (Join-Path $destination $file.Name) -Force
    }
}

$uniqueNames = $referenceFiles.Name | Sort-Object -Unique
Write-Host "Staged $($uniqueNames.Count) unique Bannerlord $Version reference assemblies."
Write-Host "Reference root: $UpstreamRoot/mb2"

param(
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath $PackageRoot).Path

function Require-File {
    param([string]$RelativePath)
    $path = Join-Path $root $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required package file missing: $RelativePath"
    }
    return $path
}

$requiredFiles = @(
    'Modules/Coop/SubModule.xml',
    'Modules/Coop/bin/Win64_Shipping_Client/Coop.dll',
    'Modules/Coop/bin/Win64_Shipping_Client/Coop.Core.dll',
    'Modules/Coop/bin/Win64_Shipping_Client/GameInterface.dll',
    'Runtime/Start-KaiTORCampaignServer.ps1',
    'Runtime/Start-KaiTORTorCampaignServer.ps1',
    'Runtime/Capture-KaiTORTorPostBattleLiveSession.ps1',
    'Runtime/Invoke-KaiTORTorPostBattleLiveAcceptance.ps1',
    'Runtime/Test-KaiTORTorPostBattleRuntime.ps1',
    'Runtime/Test-KaiTORTorPostBattleLiveEvidence.ps1',
    'Runtime/Test-KaiTORTorTestPackage.ps1',
    'Runtime/TOR_RUNTIME_CONTRACT.txt',
    'Runtime/modules.tor-1.3.15.txt',
    'BUILD.txt'
)

foreach ($relative in $requiredFiles) {
    Require-File $relative | Out-Null
}

$contract = Get-Content -LiteralPath (Join-Path $root 'Runtime/TOR_RUNTIME_CONTRACT.txt') -Raw
$contractChecks = @(
    'Bannerlord 1.3.15.110062',
    'The Old Realms 1.3.15',
    'TOR_Armory -> TOR_Environment -> TOR_Core -> Coop',
    'Admission limit: 4 simultaneous players',
    'Bannerlord.exe /singleplayer /server'
)
foreach ($needle in $contractChecks) {
    if ($contract -notlike "*$needle*") {
        throw "TOR runtime contract missing required statement: $needle"
    }
}

$moduleOrder = @(Get-Content -LiteralPath (Join-Path $root 'Runtime/modules.tor-1.3.15.txt') |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_ -and -not $_.StartsWith('#') })
$expectedOrder = @('Native','SandBoxCore','Sandbox','CustomBattle','StoryMode','TOR_Armory','TOR_Environment','TOR_Core','Coop')
if ($moduleOrder.Count -ne $expectedOrder.Count) {
    throw "Unexpected TOR module count: $($moduleOrder.Count); expected $($expectedOrder.Count)."
}
for ($i = 0; $i -lt $expectedOrder.Count; $i++) {
    if ($moduleOrder[$i] -ne $expectedOrder[$i]) {
        throw "Unexpected TOR module order at index ${i}: $($moduleOrder[$i]); expected $($expectedOrder[$i])."
    }
}

$build = Get-Content -LiteralPath (Join-Path $root 'BUILD.txt') -Raw
if ($build -notmatch 'Bannerlord 1\.3\.15\.110062') {
    throw 'BUILD.txt does not identify Bannerlord 1.3.15.110062.'
}
if ($build -notmatch 'Admission limit:\s*4 simultaneous players') {
    throw 'BUILD.txt does not preserve the tested four-player admission limit.'
}

$forbidden = Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {
    $_.Name -like 'TaleWorlds*.dll' -or
    $_.FullName -match '[\\/]Modules[\\/]TOR_(Armory|Environment|Core)[\\/]'
}
if ($forbidden) {
    $forbidden | ForEach-Object { Write-Host "Forbidden redistributed game/TOR file: $($_.FullName)" }
    throw 'Package contains Bannerlord/TOR game files that KaiTOR must not redistribute.'
}

Write-Host 'KaiTOR TOR test-package contract PASS.'
Write-Host "Package root: $root"
Write-Host 'Target: Bannerlord 1.3.15.110062 + The Old Realms 1.3.15'
Write-Host 'Admission limit: 4 simultaneous players'
Write-Host 'Authoritative TOR launch: Bannerlord.exe /singleplayer /server'

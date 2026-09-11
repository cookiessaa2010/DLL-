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
    'Modules/Coop/bin/Win64_Shipping_Client/Missions.dll',
    'Runtime/Start-KaiTORCampaignServer.ps1',
    'Runtime/Start-KaiTORTorCampaignServer.ps1',
    'Runtime/Test-KaiTORTorLiveReadiness.ps1',
    'Runtime/Capture-KaiTORTorPostBattleLiveSession.ps1',
    'Runtime/Invoke-KaiTORTorPostBattleLiveAcceptance.ps1',
    'Runtime/Test-KaiTORTorPostBattleRuntime.ps1',
    'Runtime/Test-KaiTORTorPostBattleLiveEvidence.ps1',
    'Runtime/Test-KaiTORTorTestPackage.ps1',
    'Runtime/FIRST_TOR_LIVE_TEST_RU.txt',
    'Runtime/TOR_RUNTIME_CONTRACT.txt',
    'Runtime/modules.tor-1.3.15.txt',
    'BUILD.txt',
    'SHA256SUMS.txt'
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

# Keep this validator source ASCII-only so it parses correctly in Windows PowerShell 5.1.
# Read the Russian runbook explicitly as UTF-8, but validate its critical contract using
# ASCII markers that survive consistently across PowerShell editions and Windows locales.
$runbook = Get-Content -LiteralPath (Join-Path $root 'Runtime/FIRST_TOR_LIVE_TEST_RU.txt') -Raw -Encoding UTF8
$runbookChecks = @(
    'Bannerlord: 1.3.15.110062',
    'The Old Realms: 1.3.15',
    'activeAdmissionSlots = 4',
    'Test-KaiTORTorLiveReadiness.ps1',
    'Start-KaiTORTorCampaignServer.ps1',
    'coop.debug.kaitor.snapshot4p',
    'Capture-KaiTORTorPostBattleLiveSession.ps1'
)
foreach ($needle in $runbookChecks) {
    if ($runbook -notlike "*$needle*") {
        throw "TOR live-test runbook missing required instruction: $needle"
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
if ($build -notmatch 'Target mod:\s*The Old Realms 1\.3\.15') {
    throw 'BUILD.txt does not identify The Old Realms 1.3.15.'
}
if ($build -notmatch 'Admission limit:\s*4 simultaneous players') {
    throw 'BUILD.txt does not preserve the tested four-player admission limit.'
}
if ($build -notmatch 'Source commit:\s*[0-9a-fA-F]{40}') {
    throw 'BUILD.txt does not contain a valid 40-character source commit.'
}

$forbidden = Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {
    $_.Name -like 'TaleWorlds*.dll' -or
    $_.FullName -match '[\\/]Modules[\\/]TOR_(Armory|Environment|Core)[\\/]'
}
if ($forbidden) {
    $forbidden | ForEach-Object { Write-Host "Forbidden redistributed game/TOR file: $($_.FullName)" }
    throw 'Package contains Bannerlord/TOR game files that KaiTOR must not redistribute.'
}

$sumFile = Join-Path $root 'SHA256SUMS.txt'
$sumLines = @(Get-Content -LiteralPath $sumFile | Where-Object { $_.Trim() })
if ($sumLines.Count -eq 0) {
    throw 'SHA256SUMS.txt is empty.'
}

$listedPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($line in $sumLines) {
    if ($line -notmatch '^([0-9A-Fa-f]{64})\s{2}(.+)$') {
        throw "Invalid SHA256SUMS.txt entry: $line"
    }

    $expectedHash = $Matches[1].ToUpperInvariant()
    $relative = $Matches[2]
    $normalized = $relative -replace '\\','/'
    if ($normalized -eq 'SHA256SUMS.txt') {
        throw 'SHA256SUMS.txt must not hash itself.'
    }
    if ($normalized.StartsWith('/') -or $normalized.Contains('../') -or $normalized.Contains('..\\')) {
        throw "Unsafe path in SHA256SUMS.txt: $relative"
    }
    if (-not $listedPaths.Add($normalized)) {
        throw "Duplicate path in SHA256SUMS.txt: $relative"
    }

    $filePath = Join-Path $root ($normalized -replace '/',[IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
        throw "SHA256SUMS.txt references missing file: $relative"
    }
    $actualHash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "SHA-256 mismatch for ${relative}: expected $expectedHash, got $actualHash"
    }
}

$actualFiles = @(Get-ChildItem -LiteralPath $root -Recurse -File | ForEach-Object {
    ([IO.Path]::GetRelativePath($root, $_.FullName) -replace '\\','/')
} | Where-Object { $_ -ne 'SHA256SUMS.txt' })
foreach ($relative in $actualFiles) {
    if (-not $listedPaths.Contains($relative)) {
        throw "Package file missing from SHA256SUMS.txt: $relative"
    }
}
if ($listedPaths.Count -ne $actualFiles.Count) {
    throw "SHA256SUMS.txt file count mismatch: listed $($listedPaths.Count), package contains $($actualFiles.Count) non-manifest files."
}

Write-Host 'KaiTOR TOR test-package contract PASS.'
Write-Host "Package root: $root"
Write-Host 'Target: Bannerlord 1.3.15.110062 + The Old Realms 1.3.15'
Write-Host 'Admission limit: 4 simultaneous players'
Write-Host "SHA-256 manifest verified for $($listedPaths.Count) file(s)."
Write-Host 'Authoritative TOR launch: Bannerlord.exe /singleplayer /server'

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

function Get-RelativePathCompat {
    param(
        [Parameter(Mandatory = $true)][string]$BasePath,
        [Parameter(Mandatory = $true)][string]$FullPath
    )

    $baseFull = [IO.Path]::GetFullPath($BasePath).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $targetFull = [IO.Path]::GetFullPath($FullPath)
    $prefix = $baseFull + [IO.Path]::DirectorySeparatorChar

    if (-not $targetFull.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path '$FullPath' is outside package root '$BasePath'."
    }

    return $targetFull.Substring($prefix.Length)
}

$requiredFiles = @(
    'Modules/Coop/SubModule.xml',
    'Modules/Coop/bin/Win64_Shipping_Client/Common.dll',
    'Modules/Coop/bin/Win64_Shipping_Client/Coop.dll',
    'Modules/Coop/bin/Win64_Shipping_Client/Coop.Core.dll',
    'Modules/Coop/bin/Win64_Shipping_Client/Coop.Steam.dll',
    'Modules/Coop/bin/Win64_Shipping_Client/GameInterface.dll',
    'Modules/Coop/bin/Win64_Shipping_Client/Missions.dll',
    'Modules/Coop/GUI/Prefabs/CoopConnectionUIMovie.xml',
    'Modules/Coop/GUI/Prefabs/CoopOptionsUIMovie.xml',
    'Modules/Coop/GUI/Prefabs/CoopChatUIMovie.xml',
    'Modules/Coop/GUI/Prefabs/CoopJoinCancelOverlay.xml',
    'Modules/Coop/ModuleData/Languages/std_module_strings_xml.xml',
    'Modules/Coop/ModuleData/Languages/RU/language_data.xml',
    'Modules/Coop/ModuleData/Languages/RU/std_module_strings_xml.xml',
    'START_KAITOR_COOP.bat',
    'Runtime/KaiTOR-Coop-Launcher.ps1',
    'Runtime/START_KAITOR_COOP.bat',
    'Runtime/Start-KaiTORCampaignServer.ps1',
    'Runtime/Start-KaiTORTorCampaignServer.ps1',
    'Runtime/KaiTORTorWorkshopRuntime.ps1',
    'Runtime/Prepare-KaiTORTorWorkshopLinks.ps1',
    'Runtime/Test-KaiTORTorLiveReadiness.ps1',
    'Runtime/Test-KaiTORCleaveCompatibility.ps1',
    'Runtime/Test-KaiTORStabilityCompatibility.ps1',
    'Runtime/Test-KaiTORPortraitFixCompatibility.ps1',
    'Runtime/Capture-KaiTORTorPostBattleLiveSession.ps1',
    'Runtime/Invoke-KaiTORTorPostBattleLiveAcceptance.ps1',
    'Runtime/Test-KaiTORTorPostBattleRuntime.ps1',
    'Runtime/Test-KaiTORTorPostBattleLiveEvidence.ps1',
    'Runtime/Test-KaiTORTorTestPackage.ps1',
    'Runtime/FIRST_TOR_LIVE_TEST_RU.txt',
    'Runtime/TOR_RUNTIME_CONTRACT.txt',
    'Runtime/modules.tor-1.3.15.txt',
    'README_EN.txt',
    'README_RU.txt',
    'BUILD.txt',
    'SHA256SUMS.txt'
)

foreach ($relative in $requiredFiles) {
    Require-File $relative | Out-Null
}

# Release metadata and bilingual UI are part of the KaiTOR module contract.
[xml]$moduleXml = Get-Content -LiteralPath (Join-Path $root 'Modules/Coop/SubModule.xml') -Raw
if ([string]$moduleXml.Module.Id.value -ne 'Coop') {
    throw "KaiTOR network-compatible module Id must remain Coop."
}
if ([string]$moduleXml.Module.Name.value -ne 'KaiTOR Co-op') {
    throw "Unexpected module display name: $([string]$moduleXml.Module.Name.value)"
}
if ([string]$moduleXml.Module.Version.value -ne 'v1.3.15.12') {
    throw "Unexpected KaiTOR module version: $([string]$moduleXml.Module.Version.value)"
}
$moduleDeps = @($moduleXml.Module.DependedModules.DependedModule | ForEach-Object { [string]$_.Id })
foreach ($requiredDep in @('Native','SandBoxCore','Sandbox','CustomBattle','StoryMode','TOR_Armory','TOR_Environment','TOR_Core')) {
    if ($moduleDeps -notcontains $requiredDep) {
        throw "KaiTOR SubModule.xml missing dependency: $requiredDep"
    }
}

$englishStrings = Get-Content -LiteralPath (Join-Path $root 'Modules/Coop/ModuleData/Languages/std_module_strings_xml.xml') -Raw -Encoding UTF8
$russianStrings = Get-Content -LiteralPath (Join-Path $root 'Modules/Coop/ModuleData/Languages/RU/std_module_strings_xml.xml') -Raw -Encoding UTF8
$russianManifest = Get-Content -LiteralPath (Join-Path $root 'Modules/Coop/ModuleData/Languages/RU/language_data.xml') -Raw -Encoding UTF8
foreach ($id in @(
    'kaitor_menu_host',
    'kaitor_menu_join',
    'kaitor_join_header',
    'kaitor_direct',
    'kaitor_steam_lobbies',
    'kaitor_server_name',
    'kaitor_server_search_placeholder',
    'kaitor_chat',
    'kaitor_options_header',
    'kaitor_server_visibility',
    'kaitor_server_password_title',
    'kaitor_connecting_title',
    'kaitor_hosting_title',
    'kaitor_applying_patches',
    'kaitor_validating_modules',
    'kaitor_coop_options',
    'kaitor_invite_friends',
    'kaitor_report_bug',
    'kaitor_bug_share_title',
    'kaitor_crash_reports_title',
    'kaitor_credits',
    'kaitor_donate_prompt'
)) {
    if ($englishStrings -notmatch ('id="' + [regex]::Escape($id) + '"')) { throw "English localization missing id: $id" }
    if ($russianStrings -notmatch ('id="' + [regex]::Escape($id) + '"')) { throw "Russian localization missing id: $id" }
}
if ($russianManifest -notmatch 'LanguageData id="Русский"') {
    throw 'Russian language manifest does not register Русский.'
}


# Both language packs must expose exactly the same KaiTOR string ids.
[xml]$englishXml = $englishStrings
[xml]$russianXml = $russianStrings
$englishIds = @($englishXml.base.strings.string | ForEach-Object { [string]$_.id } | Sort-Object -Unique)
$russianIds = @($russianXml.base.strings.string | ForEach-Object { [string]$_.id } | Sort-Object -Unique)
$missingRussian = @($englishIds | Where-Object { $russianIds -notcontains $_ })
$missingEnglish = @($russianIds | Where-Object { $englishIds -notcontains $_ })
if ($missingRussian.Count -gt 0 -or $missingEnglish.Count -gt 0) {
    throw "RU/EN localization id mismatch. Missing RU: $($missingRussian -join ', '); missing EN: $($missingEnglish -join ', ')"
}

# Visible prefab copy must be data-bound/localized. Strip XML comments first so commented
# upstream reference text does not trigger the release gate. "(i)" and the wager arrow are
# language-neutral UI glyphs.
$allowedLiteralUiText = @('(i)', '-&gt;')
$prefabRoot = Join-Path $root 'Modules/Coop/GUI/Prefabs'
foreach ($prefab in Get-ChildItem -LiteralPath $prefabRoot -File -Filter '*.xml') {
    $prefabText = Get-Content -LiteralPath $prefab.FullName -Raw -Encoding UTF8
    $activePrefabText = [regex]::Replace($prefabText, '<!--.*?-->', '', [Text.RegularExpressions.RegexOptions]::Singleline)
    $matches = [regex]::Matches($activePrefabText, '\b[A-Za-z0-9_.]*Text="([^"]+)"')
    foreach ($match in $matches) {
        $value = $match.Groups[1].Value
        if ($value.StartsWith('@') -or $value.StartsWith('{=')) { continue }
        if ($allowedLiteralUiText -contains $value) { continue }
        if ($value -match '[A-Za-z]') {
            throw "Unlocalized visible prefab text '$value' in $($prefab.Name)."
        }
    }
}

$connectPrefab = Get-Content -LiteralPath (Join-Path $root 'Modules/Coop/GUI/Prefabs/CoopConnectionUIMovie.xml') -Raw -Encoding UTF8
if ($connectPrefab -notmatch 'KaiTORFrame' -or $connectPrefab -notmatch '@BrandSubtitleText') {
    throw 'KaiTOR dark-fantasy connection UI frame is missing.'
}

$debugSymbols = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.pdb')
if ($debugSymbols.Count -gt 0) {
    $debugSymbols | ForEach-Object { Write-Host "Unexpected debug symbol in release package: $($_.FullName)" }
    throw 'Release package contains PDB debug symbols.'
}

$readmeEn = Get-Content -LiteralPath (Join-Path $root 'README_EN.txt') -Raw -Encoding UTF8
$readmeRu = Get-Content -LiteralPath (Join-Path $root 'README_RU.txt') -Raw -Encoding UTF8
foreach ($needle in @('START_KAITOR_COOP.bat','coop.debug.kaitor.snapshot4p','D -> B -> A -> C')) {
    if ($readmeEn -notlike "*$needle*") { throw "English quick start missing: $needle" }
    if ($readmeRu -notlike "*$needle*") { throw "Russian quick start missing: $needle" }
}

# Parse the tester-facing launcher without executing it. This catches packaging/encoding/syntax
# failures before the archive is published while keeping CI headless.
$launcherPath = Join-Path $root 'Runtime/KaiTOR-Coop-Launcher.ps1'
$launcherTokens = $null
$launcherErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile(
    $launcherPath,
    [ref]$launcherTokens,
    [ref]$launcherErrors) | Out-Null
if (@($launcherErrors).Count -gt 0) {
    $launcherErrors | ForEach-Object { Write-Host "Launcher parse error: $($_.Message)" }
    throw 'KaiTOR-Coop-Launcher.ps1 contains PowerShell syntax errors.'
}
$launcherText = Get-Content -LiteralPath $launcherPath -Raw -Encoding UTF8
foreach ($needle in @(
    'KaiTOR Co-op — Панель сервера',
    'KaiTOR Co-op - Server Control',
    'Start-KaiTORTorCampaignServer.ps1',
    'Test-KaiTORTorLiveReadiness.ps1',
    '4200 UDP',
    'KaiTOR Co-op | TOR RU Test',
    'Steam Lobby / UDP 4200',
    'RequireSteamHost',
    'steam_ready'
)) {
    if ($launcherText -notlike "*$needle*") {
        throw "KaiTOR server panel missing required RU/EN/runtime marker: $needle"
    }
}

$rootBat = Get-Content -LiteralPath (Join-Path $root 'START_KAITOR_COOP.bat') -Raw
if ($rootBat -notmatch 'KaiTOR-Coop-Launcher\.ps1') {
    throw 'START_KAITOR_COOP.bat does not launch the server control panel.'
}

$contract = Get-Content -LiteralPath (Join-Path $root 'Runtime/TOR_RUNTIME_CONTRACT.txt') -Raw
$contractChecks = @(
    'Bannerlord 1.3.15.110062',
    'The Old Realms 1.3.15',
    'TOR_Armory -> TOR_Environment -> TOR_Core -> Coop',
    'Admission limit: 4 simultaneous players',
    'Bannerlord.exe /singleplayer /server',
    'KaiCleave v1.3.15.31',
    'KaiTOR_Stability v1.3.15.50',
    'KaiTOR_PortraitFix v1.3.15.60'
)
foreach ($needle in $contractChecks) {
    if ($contract -notlike "*$needle*") {
        throw "TOR runtime contract missing required statement: $needle"
    }
}

$cleaveCompatibility = Get-Content -LiteralPath (Join-Path $root 'Runtime/Test-KaiTORCleaveCompatibility.ps1') -Raw
if ($cleaveCompatibility -notmatch [regex]::Escape('$ExpectedVersion = ''v1.3.15.31''')) {
    throw 'KaiCleave compatibility gate is not pinned to the current Steam-compatible v1.3.15.31 build.'
}
$stabilityCompatibility = Get-Content -LiteralPath (Join-Path $root 'Runtime/Test-KaiTORStabilityCompatibility.ps1') -Raw
if ($stabilityCompatibility -notmatch [regex]::Escape('$ExpectedVersion = ''v1.3.15.50''')) {
    throw 'KaiTOR Stability compatibility gate is not pinned to the current Steam-compatible v1.3.15.50 build.'
}

$portraitCompatibility = Get-Content -LiteralPath (Join-Path $root 'Runtime/Test-KaiTORPortraitFixCompatibility.ps1') -Raw
if ($portraitCompatibility -notmatch [regex]::Escape('$ExpectedVersion = ''v1.3.15.60''')) {
    throw 'KaiTOR Portrait Fix compatibility gate is not pinned to the current Steam-compatible v1.3.15.60 build.'
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
    'TOR Workshop staging',
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
    ((Get-RelativePathCompat -BasePath $root -FullPath $_.FullName) -replace '\\','/')
} | Where-Object { $_ -ne 'SHA256SUMS.txt' })
foreach ($relative in $actualFiles) {
    if (-not $listedPaths.Contains($relative)) {
        throw "Package file missing from SHA256SUMS.txt: $relative"
    }
}
if ($listedPaths.Count -ne $actualFiles.Count) {
    throw "SHA256SUMS.txt file count mismatch: listed $($listedPaths.Count), package contains $($actualFiles.Count) non-manifest files."
}

Write-Host 'KaiTOR TOR full-module package contract PASS.'
Write-Host "Package root: $root"
Write-Host 'Target: Bannerlord 1.3.15.110062 + The Old Realms 1.3.15'
Write-Host 'Admission limit: 4 simultaneous players'
Write-Host "SHA-256 manifest verified for $($listedPaths.Count) file(s)."
Write-Host 'Authoritative TOR launch: Bannerlord.exe /singleplayer /server'

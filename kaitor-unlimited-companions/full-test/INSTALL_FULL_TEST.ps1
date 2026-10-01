param(
    [string]$GameDir,
    [switch]$NonInteractive
)

$ErrorActionPreference = "Stop"
$ItemId = "3809043415"
$AppId = "261550"
$ModId = "KaiTOR_UnlimitedCompanions"
$ExpectedVersion = "v1.3.15.09"

function Write-Step([string]$Text) {
    Write-Host ""
    Write-Host "== $Text ==" -ForegroundColor Cyan
}

function Find-Bannerlord {
    param([string]$Explicit)

    $candidates = @()
    if ($Explicit) { $candidates += $Explicit }

    $candidates += @(
        "D:\steam\steamapps\common\Mount & Blade II Bannerlord",
        "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord",
        "C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord"
    )

    try {
        $steamPath = (Get-ItemProperty -Path "HKCU:\Software\Valve\Steam" -Name SteamPath -ErrorAction Stop).SteamPath
        if ($steamPath) {
            $candidates += (Join-Path $steamPath "steamapps\common\Mount & Blade II Bannerlord")
        }
    } catch {}

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if ($candidate -and (Test-Path (Join-Path $candidate "bin\Win64_Shipping_Client\Bannerlord.exe"))) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw "Не найден Mount & Blade II Bannerlord. Запусти: INSTALL_FULL_TEST.bat или передай -GameDir 'D:\...\Mount & Blade II Bannerlord'."
}

function Backup-Folder {
    param(
        [string]$Path,
        [string]$Label,
        [string]$BackupRoot
    )

    if (-not (Test-Path $Path)) { return $null }

    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $safeLabel = $Label -replace '[^A-Za-z0-9_.-]', '_'
    $dest = Join-Path $BackupRoot "$safeLabel-$stamp"
    New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null
    Move-Item -LiteralPath $Path -Destination $dest -Force
    Write-Host "Резервная копия: $dest" -ForegroundColor DarkGray
    return $dest
}

function Get-ModuleVersion {
    param([string]$ModuleDir)
    $xml = Join-Path $ModuleDir "SubModule.xml"
    if (-not (Test-Path $xml)) { return $null }
    try {
        [xml]$doc = Get-Content -LiteralPath $xml -Raw
        return [string]$doc.Module.Version.value
    } catch {
        return $null
    }
}

$packageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$bundleModules = Join-Path $packageRoot "Modules"
$sourceKai = Join-Path $bundleModules $ModId
$sourceUi = Join-Path $bundleModules "Bannerlord.UIExtenderEx"

if (-not (Test-Path (Join-Path $sourceKai "SubModule.xml"))) {
    throw "В полном архиве отсутствует Modules\$ModId\SubModule.xml"
}
if (-not (Test-Path (Join-Path $sourceKai "bin\Win64_Shipping_Client\KaiTOR_UnlimitedCompanions.dll"))) {
    throw "В полном архиве отсутствует KaiTOR_UnlimitedCompanions.dll"
}
if (-not (Test-Path (Join-Path $sourceUi "SubModule.xml"))) {
    throw "В полном архиве отсутствует официальный Bannerlord.UIExtenderEx."
}

$game = Find-Bannerlord -Explicit $GameDir
$modulesDir = Join-Path $game "Modules"
$steamApps = Split-Path (Split-Path $game -Parent) -Parent
$workshopTarget = Join-Path $steamApps "workshop\content\$AppId\$ItemId"
$localTarget = Join-Path $modulesDir $ModId
$uiTarget = Join-Path $modulesDir "Bannerlord.UIExtenderEx"
$backupRoot = Join-Path $env:LOCALAPPDATA "KaiTOR\UnlimitedCompanions\TestBackups"

Write-Host "Kai TOR Unlimited Companions $ExpectedVersion - FULL LIVE TEST INSTALLER" -ForegroundColor Yellow
Write-Host "Game: $game"
Write-Host "Workshop item: $ItemId"

Write-Step "Убираю дубликаты Kai TOR Unlimited Companions"
if (Test-Path $localTarget) {
    Backup-Folder -Path $localTarget -Label "local-$ModId" -BackupRoot $backupRoot | Out-Null
}

if (Test-Path $workshopTarget) {
    Backup-Folder -Path $workshopTarget -Label "workshop-$ItemId" -BackupRoot $backupRoot | Out-Null
    New-Item -ItemType Directory -Path $workshopTarget -Force | Out-Null
    Copy-Item -Path (Join-Path $sourceKai "*") -Destination $workshopTarget -Recurse -Force
    $kaiTarget = $workshopTarget
    Write-Host "Тестовая версия установлена прямо в Workshop item $ItemId, поэтому второй копии модуля в лаунчере не будет." -ForegroundColor Green
} else {
    New-Item -ItemType Directory -Path $localTarget -Force | Out-Null
    Copy-Item -Path (Join-Path $sourceKai "*") -Destination $localTarget -Recurse -Force
    $kaiTarget = $localTarget
    Write-Warning "Workshop item $ItemId не найден. Мод установлен локально. Если Steam снова скачает Workshop-копию, появится дубликат — на время теста отпишись от Workshop item."
}

Write-Step "Устанавливаю UIExtenderEx"
$needUiInstall = $true
if (Test-Path $uiTarget) {
    $existing = Get-ModuleVersion -ModuleDir $uiTarget
    if ($existing) {
        try {
            $existingVer = [version](($existing -replace '^v','') -replace '\.0+(?=\d)','.')
            $requiredVer = [version]"2.13.3"
            if ($existingVer -ge $requiredVer) {
                Write-Host "UIExtenderEx уже установлен: $existing. Оставляю его без изменений." -ForegroundColor Green
                $needUiInstall = $false
            }
        } catch {}
    }

    if ($needUiInstall) {
        Backup-Folder -Path $uiTarget -Label "Bannerlord.UIExtenderEx" -BackupRoot $backupRoot | Out-Null
    }
}

if ($needUiInstall) {
    New-Item -ItemType Directory -Path $uiTarget -Force | Out-Null
    Copy-Item -Path (Join-Path $sourceUi "*") -Destination $uiTarget -Recurse -Force
    Write-Host "Установлен Bannerlord.UIExtenderEx из полного тестового пакета." -ForegroundColor Green
}

Write-Step "Проверка"
$installedVersion = Get-ModuleVersion -ModuleDir $kaiTarget
if ($installedVersion -ne $ExpectedVersion) {
    throw "Установлена неожиданная версия Kai TOR: $installedVersion (ожидалась $ExpectedVersion)"
}

$uiInstalledVersion = Get-ModuleVersion -ModuleDir $uiTarget
if (-not $uiInstalledVersion) {
    throw "UIExtenderEx установлен некорректно: не читается SubModule.xml"
}

Write-Host ""
Write-Host "ГОТОВО." -ForegroundColor Green
Write-Host "Kai TOR Unlimited Companions: $installedVersion"
Write-Host "Bannerlord.UIExtenderEx: $uiInstalledVersion"
Write-Host ""
Write-Host "В лаунчере оставь ОДНУ строку Kai TOR Unlimited Companions и включи в порядке:" -ForegroundColor Yellow
Write-Host "1. Bannerlord.Harmony"
Write-Host "2. Bannerlord.UIExtenderEx"
Write-Host "3. Kai TOR Unlimited Companions"
Write-Host "4. The Old Realms / TOR модули в твоём обычном порядке"
Write-Host ""
Write-Host "Ожидаемая версия Kai TOR в лаунчере: v1.3.15.9"
Write-Host "Резервные копии старых папок: $backupRoot"
Write-Host ""
if (-not $NonInteractive) {
    Read-Host "Нажми Enter для выхода"
}

param(
    [string]$DllPath = "",
    [string]$TargetDir = "",
    [switch]$Restore
)

$ErrorActionPreference = "Stop"

# Supported build: Mount & Blade II: Bannerlord v1.3.15.110062
$OriginalSha = "9589f5b59c9649461817ac04620e942303590ce93d0b643d17cababd8f581bd3"

# File offsets for TaleWorlds.Native.dll from v1.3.15.110062.
$OffCallCommonAppData = 0x0C557B
$OffInitialAppend     = 0x0C5580
$OffPathLength        = 0x0C55A3
$OffLiteralLoad       = 0x0C55B7
$OffLiteralCopyRest   = 0x0C55C1
$OffShaderSuffix      = 0x0C55E5
$OffPathLiteral       = 0x00ADD6A8
$LiteralCapacity      = 16 # 15 ASCII bytes + NUL

function Get-Sha256([string]$Path) {
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

function Hex-ToBytes([string]$Hex) {
    $Hex = $Hex -replace '\s',''
    if (($Hex.Length % 2) -ne 0) { throw "Invalid hex string length." }
    $result = New-Object byte[] ($Hex.Length / 2)
    for ($i = 0; $i -lt $result.Length; $i++) {
        $result[$i] = [Convert]::ToByte($Hex.Substring($i * 2, 2), 16)
    }
    return $result
}

function Bytes-Equal([byte[]]$Data, [int]$Offset, [byte[]]$Expected) {
    if (($Offset + $Expected.Length) -gt $Data.Length) { return $false }
    for ($i = 0; $i -lt $Expected.Length; $i++) {
        if ($Data[$Offset + $i] -ne $Expected[$i]) { return $false }
    }
    return $true
}

function Assert-Bytes([byte[]]$Data, [int]$Offset, [byte[]]$Expected, [string]$Name) {
    if (-not (Bytes-Equal $Data $Offset $Expected)) {
        throw ("Byte check failed for '{0}' at file offset 0x{1:X}. This DLL is not the expected v1.3.15.110062 build." -f $Name, $Offset)
    }
}

function Write-Bytes([byte[]]$Data, [int]$Offset, [byte[]]$Bytes) {
    [Array]::Copy($Bytes, 0, $Data, $Offset, $Bytes.Length)
}

function Fill-Nops([byte[]]$Data, [int]$Offset, [int]$Length) {
    for ($i = 0; $i -lt $Length; $i++) { $Data[$Offset + $i] = 0x90 }
}

function Is-NopRange([byte[]]$Data, [int]$Offset, [int]$Length) {
    if (($Offset + $Length) -gt $Data.Length) { return $false }
    for ($i = 0; $i -lt $Length; $i++) {
        if ($Data[$Offset + $i] -ne 0x90) { return $false }
    }
    return $true
}

function Is-KaiPatched([byte[]]$Data) {
    if (-not (Is-NopRange $Data $OffCallCommonAppData 5)) { return $false }
    if (-not (Is-NopRange $Data $OffInitialAppend 35)) { return $false }
    if (-not (Bytes-Equal $Data $OffPathLength ([byte[]](0x83,0xC3)))) { return $false }
    $pathLen = [int]$Data[$OffPathLength + 2]
    if (($pathLen -lt 1) -or ($pathLen -gt 15)) { return $false }
    if (-not (Bytes-Equal $Data $OffLiteralLoad (Hex-ToBytes "0F 10 05 EA 82 A1 00"))) { return $false }
    if (-not (Is-NopRange $Data $OffLiteralCopyRest 33)) { return $false }
    if (-not (Is-NopRange $Data $OffShaderSuffix 46)) { return $false }
    if ($Data[$OffPathLiteral + $pathLen] -ne 0x00) { return $false }
    return $true
}

function Get-KaiPatchedTarget([byte[]]$Data) {
    if (-not (Is-KaiPatched $Data)) { return $null }
    $pathLen = [int]$Data[$OffPathLength + 2]
    $bytes = New-Object byte[] $pathLen
    [Array]::Copy($Data, $OffPathLiteral, $bytes, 0, $pathLen)
    return [Text.Encoding]::ASCII.GetString($bytes)
}

function Resolve-ConfiguredTarget([string]$ExplicitTarget) {
    $candidate = $ExplicitTarget

    if ([string]::IsNullOrWhiteSpace($candidate)) {
        $iniPath = Join-Path $PSScriptRoot "ShaderRedirector.ini"
        if (-not (Test-Path -LiteralPath $iniPath)) {
            throw "ShaderRedirector.ini was not found."
        }

        foreach ($line in Get-Content -LiteralPath $iniPath) {
            $trimmed = $line.Trim()
            if (($trimmed.Length -eq 0) -or $trimmed.StartsWith("#") -or $trimmed.StartsWith(";") -or $trimmed.StartsWith("[")) {
                continue
            }
            if ($trimmed -match '^Path\s*=\s*(.+)$') {
                $candidate = $Matches[1].Trim()
                break
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($candidate)) {
        throw "No shader cache path was configured. Set Path= in ShaderRedirector.ini."
    }

    $candidate = $candidate.Trim().Trim('"').Trim("'")
    $candidate = [Environment]::ExpandEnvironmentVariables($candidate)

    # This native patch stores the replacement path in a 16-byte literal slot.
    # Keep the public patch deliberately conservative: local drive path, printable ASCII, max 15 bytes including trailing slash.
    if ($candidate -notmatch '^[A-Za-z]:\\') {
        throw "Target path must be an absolute local drive path, for example D:\MNB\Shaders."
    }
    if ($candidate -match '[^\x20-\x7E]') {
        throw "Target path must contain ASCII characters only."
    }

    $candidate = $candidate.TrimEnd('\') + '\'
    $bytes = [Text.Encoding]::ASCII.GetBytes($candidate)
    if ($bytes.Length -gt 15) {
        throw ("Target path is too long for this build's safe inline patch: {0} bytes. Maximum is 15 bytes including the final backslash. Try a shorter path such as D:\Shaders\." -f $bytes.Length)
    }

    return $candidate
}

if ([string]::IsNullOrWhiteSpace($DllPath)) {
    $localDll = Join-Path $PSScriptRoot "TaleWorlds.Native.dll"
    if (Test-Path -LiteralPath $localDll) {
        $DllPath = $localDll
    } else {
        Write-Host ""
        Write-Host "TaleWorlds.Native.dll was not found next to the patcher." -ForegroundColor Yellow
        Write-Host "Copy the patcher files into:" -ForegroundColor Yellow
        Write-Host "  Mount & Blade II Bannerlord\bin\Win64_Shipping_Client\" -ForegroundColor Cyan
        Write-Host "and run PATCH.bat again." -ForegroundColor Yellow
        Write-Host ""
        exit 2
    }
}

$DllPath = (Resolve-Path -LiteralPath $DllPath).Path
$BackupPath = "$DllPath.KaiOriginal.bak"

Write-Host "Bannerlord Shader Cache Redirector" -ForegroundColor Cyan
Write-Host "Supported build: v1.3.15.110062" -ForegroundColor Cyan
Write-Host "DLL: $DllPath"
Write-Host ""

if ($Restore) {
    if (-not (Test-Path -LiteralPath $BackupPath)) {
        throw "Backup not found: $BackupPath"
    }

    $backupHash = Get-Sha256 $BackupPath
    if ($backupHash -ne $OriginalSha) {
        throw "Backup SHA-256 is not the expected original build. Restore cancelled."
    }

    $currentData = [System.IO.File]::ReadAllBytes($DllPath)
    $currentHash = Get-Sha256 $DllPath
    if (($currentHash -ne $OriginalSha) -and (-not (Is-KaiPatched $currentData))) {
        throw "Current TaleWorlds.Native.dll is neither the supported original nor a recognized redirector patch. Restore cancelled to avoid overwriting a game update or another mod."
    }

    Copy-Item -LiteralPath $BackupPath -Destination $DllPath -Force
    if ((Get-Sha256 $DllPath) -ne $OriginalSha) { throw "Restore verification failed." }

    Write-Host "Original DLL restored successfully." -ForegroundColor Green
    exit 0
}

$TargetDir = Resolve-ConfiguredTarget $TargetDir
$targetBytes = [Text.Encoding]::ASCII.GetBytes($TargetDir)
Write-Host "Shader directory: $TargetDir"
Write-Host ""

$currentData = [System.IO.File]::ReadAllBytes($DllPath)
$currentHash = Get-Sha256 $DllPath
$currentIsOriginal = ($currentHash -eq $OriginalSha)
$currentIsKaiPatch = Is-KaiPatched $currentData

if (-not $currentIsOriginal -and -not $currentIsKaiPatch) {
    Write-Host "Found SHA-256: $currentHash" -ForegroundColor Red
    throw "Unsupported TaleWorlds.Native.dll. This patch supports only the exact original Bannerlord v1.3.15.110062 DLL or a DLL previously patched by this tool."
}

if ($currentIsKaiPatch) {
    if (-not (Test-Path -LiteralPath $BackupPath)) {
        throw "A redirector patch is present, but the verified original backup is missing. Refusing to modify the DLL further. Restore/verify the game first."
    }
    if ((Get-Sha256 $BackupPath) -ne $OriginalSha) {
        throw "The existing backup does not match the supported original DLL. Refusing to continue."
    }

    $existingTarget = Get-KaiPatchedTarget $currentData
    if ($existingTarget -eq $TargetDir) {
        New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
        Write-Host "Patch is already installed for this target." -ForegroundColor Green
        Write-Host "Shaders are redirected to: $TargetDir" -ForegroundColor Green
        exit 0
    }

    Write-Host "Existing redirect target: $existingTarget" -ForegroundColor Yellow
    Write-Host "Updating redirect target to: $TargetDir" -ForegroundColor Yellow
} else {
    # Exact byte checks from the original supported build.
    Assert-Bytes $currentData 0x0C557B (Hex-ToBytes "E8 00 6A 65 00") "CommonAppData conversion call"
    Assert-Bytes $currentData 0x0C5580 (Hex-ToBytes "8B 5F 10 FF C3 8B D3 48 8B CF E8 E1 50 65 00 8B 4F 10 48 03 4F 08 0F B7 05 F3 1E A1 00 66 89 01 89 5F 10") "initial slash append"
    Assert-Bytes $currentData 0x0C55A3 (Hex-ToBytes "83 C3 1D") "product path length"
    Assert-Bytes $currentData 0x0C55B7 (Hex-ToBytes "0F 10 05 4A 1C A1 00") "product literal load"
    Assert-Bytes $currentData 0x0C55C1 (Hex-ToBytes "F2 0F 10 0D 4F 1C A1 00 F2 0F 11 49 10 8B 05 4C 1C A1 00 89 41 18 0F B7 05 46 1C A1 00 66 89 41 1C") "remaining product literal copy"
    Assert-Bytes $currentData 0x0C55E5 (Hex-ToBytes "83 C3 09 8B D3 48 8B CF E8 7E 50 65 00 8B 57 10 48 03 57 08 F2 0F 10 05 A7 82 A1 00 F2 0F 11 02 0F B7 0D A4 82 A1 00 66 89 4A 08 89 5F 10") "Shaders suffix append"
    Assert-Bytes $currentData 0x00ADD6A8 (Hex-ToBytes "2F 53 68 61 64 65 72 73 2F 00 00 00 00 00 00 00") "Shaders literal"

    if (-not (Test-Path -LiteralPath $BackupPath)) {
        Copy-Item -LiteralPath $DllPath -Destination $BackupPath
        Write-Host "Backup created: $BackupPath"
    } else {
        if ((Get-Sha256 $BackupPath) -ne $OriginalSha) {
            throw "An existing backup has an unexpected hash. Rename/remove it manually before patching."
        }
        Write-Host "Existing verified backup will be kept: $BackupPath"
    }
}

$data = [byte[]]$currentData.Clone()

# Redirect the native path constructor to one short absolute path stored in the existing 16-byte literal slot.
Fill-Nops $data $OffCallCommonAppData 5
Fill-Nops $data $OffInitialAppend 35
Write-Bytes $data $OffPathLength ([byte[]](0x83, 0xC3, [byte]$targetBytes.Length))
Write-Bytes $data $OffLiteralLoad (Hex-ToBytes "0F 10 05 EA 82 A1 00")
Fill-Nops $data $OffLiteralCopyRest 33
Fill-Nops $data $OffShaderSuffix 46

$literal = New-Object byte[] $LiteralCapacity
[Array]::Copy($targetBytes, 0, $literal, 0, $targetBytes.Length)
Write-Bytes $data $OffPathLiteral $literal

New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null

$tempPath = "$DllPath.KaiPatch.tmp"
[System.IO.File]::WriteAllBytes($tempPath, $data)

$verifyData = [System.IO.File]::ReadAllBytes($tempPath)
if (-not (Is-KaiPatched $verifyData)) {
    Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
    throw "Patched DLL signature verification failed. Original DLL was NOT replaced."
}
$verifyTarget = Get-KaiPatchedTarget $verifyData
if ($verifyTarget -ne $TargetDir) {
    Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
    throw "Patched path verification failed. Original DLL was NOT replaced."
}

Move-Item -LiteralPath $tempPath -Destination $DllPath -Force

$finalData = [System.IO.File]::ReadAllBytes($DllPath)
if (-not (Is-KaiPatched $finalData)) { throw "Final verification failed after replacement." }
if ((Get-KaiPatchedTarget $finalData) -ne $TargetDir) { throw "Final path verification failed after replacement." }

Write-Host ""
Write-Host "PATCH INSTALLED SUCCESSFULLY" -ForegroundColor Green
Write-Host "Bannerlord shader cache path is now:" -ForegroundColor Green
Write-Host "  $TargetDir" -ForegroundColor Cyan
Write-Host ""
Write-Host "Original DLL backup:" -ForegroundColor DarkGray
Write-Host "  $BackupPath" -ForegroundColor DarkGray
Write-Host "Use RESTORE.bat before multiplayer, verifying game files, or after a Bannerlord update." -ForegroundColor Yellow

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$TaomArmoryPath,
    [string]$TorArmoryPath,
    [switch]$DeepScan,
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$requiredXmlTokens = @(
    'dwarf_skeleton_a',
    'as_dwarf_female_warrior',
    'sk_dwarf_bm_f1_body',
    'sk_dwarf_bm_f1_shoulder',
    'sk_dwarf_bm_f1_head',
    'sk_dwarf_underwear_female_a'
)

function Get-TextFiles([string]$root) {
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw "Directory not found: $root"
    }
    @(Get-ChildItem -LiteralPath $root -Recurse -File -Include *.xml,*.xslt,*.txt,*.md -ErrorAction SilentlyContinue)
}

function Find-TokenInTextFiles([System.IO.FileInfo[]]$files, [string]$token) {
    foreach ($file in $files) {
        try {
            if (Select-String -LiteralPath $file.FullName -SimpleMatch -Pattern $token -Quiet) {
                return $file.FullName
            }
        } catch { }
    }
    return $null
}

function Test-BinaryContainsAscii([string]$path, [string]$needle) {
    $pattern = [Text.Encoding]::ASCII.GetBytes($needle)
    if ($pattern.Length -eq 0) { return $true }

    $stream = [IO.File]::OpenRead($path)
    try {
        $chunkSize = 4MB
        $buffer = New-Object byte[] ($chunkSize + $pattern.Length)
        $carry = 0
        while (($read = $stream.Read($buffer, $carry, $chunkSize)) -gt 0) {
            $length = $carry + $read
            for ($i = 0; $i -le $length - $pattern.Length; $i++) {
                $ok = $true
                for ($j = 0; $j -lt $pattern.Length; $j++) {
                    if ($buffer[$i + $j] -ne $pattern[$j]) { $ok = $false; break }
                }
                if ($ok) { return $true }
            }

            $carry = [Math]::Min($pattern.Length - 1, $length)
            if ($carry -gt 0) {
                [Array]::Copy($buffer, $length - $carry, $buffer, 0, $carry)
            }
        }
    }
    finally {
        $stream.Dispose()
    }
    return $false
}

function Find-TokenInTpac([string]$root, [string]$token) {
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Filter *.tpac -ErrorAction SilentlyContinue) {
        try {
            if (Test-BinaryContainsAscii $file.FullName $token) { return $file.FullName }
        } catch { }
    }
    return $null
}

$taomTextFiles = Get-TextFiles $TaomArmoryPath
$tokenResults = [ordered]@{}
foreach ($token in $requiredXmlTokens) {
    $textHit = Find-TokenInTextFiles $taomTextFiles $token
    $binaryHit = $null
    if ($DeepScan) { $binaryHit = Find-TokenInTpac $TaomArmoryPath $token }
    $tokenResults[$token] = [ordered]@{
        xml_or_text = $textHit
        tpac = $binaryHit
        referenced = [bool]$textHit
        packaged = if ($DeepScan) { [bool]$binaryHit } else { $null }
    }
}

$missingRefs = @($requiredXmlTokens | Where-Object { -not $tokenResults[$_].referenced })
$missingPackaged = @()
if ($DeepScan) {
    $missingPackaged = @($requiredXmlTokens | Where-Object { -not $tokenResults[$_].packaged })
}

$torSkeletonEvidence = $null
if ($TorArmoryPath) {
    $torTextFiles = Get-TextFiles $TorArmoryPath
    $torSkeletonEvidence = Find-TokenInTextFiles $torTextFiles 'dwarf_skeleton_a'
    if (-not $torSkeletonEvidence -and $DeepScan) {
        $torSkeletonEvidence = Find-TokenInTpac $TorArmoryPath 'dwarf_skeleton_a'
    }
}

$status = 'CANDIDATE'
if ($missingRefs.Count -gt 0) {
    $status = 'BLOCKED_MISSING_TAOM_REFERENCES'
} elseif ($DeepScan -and $missingPackaged.Count -gt 0) {
    $status = 'BLOCKED_MISSING_TPAC_RESOURCES'
} elseif ($TorArmoryPath -and -not $torSkeletonEvidence) {
    $status = 'BLOCKED_TOR_SKELETON_NOT_CONFIRMED'
} elseif ($TorArmoryPath -and $torSkeletonEvidence) {
    $status = 'READY_FOR_MANUAL_RIG_TEST'
}

$report = [ordered]@{
    status = $status
    taom_armory = (Resolve-Path -LiteralPath $TaomArmoryPath).Path
    tor_armory = if ($TorArmoryPath) { (Resolve-Path -LiteralPath $TorArmoryPath).Path } else { $null }
    deep_scan = [bool]$DeepScan
    tokens = $tokenResults
    missing_references = $missingRefs
    missing_packaged_resources = $missingPackaged
    tor_dwarf_skeleton_evidence = $torSkeletonEvidence
    note = 'READY_FOR_MANUAL_RIG_TEST is not release approval. TOR and TAOM dwarf rigs still require an in-game armour/body animation test before female Dawi pregnancy is enabled.'
}

$json = $report | ConvertTo-Json -Depth 6
$json

if ($ReportPath) {
    $parent = Split-Path -Parent $ReportPath
    if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    Set-Content -LiteralPath $ReportPath -Value $json -Encoding UTF8
}

if ($status -like 'BLOCKED_*') { exit 2 }
exit 0

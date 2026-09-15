[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$TaomArmoryPath,
    [Parameter(Mandatory=$true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$requiredResources = @(
    'dwarf_skeleton_a',
    'sk_dwarf_bm_f1_body',
    'sk_dwarf_bm_f1_shoulder',
    'sk_dwarf_bm_f1_head',
    'sk_dwarf_underwear_female_a'
)

function Test-BinaryContainsAscii([string]$path, [string]$needle) {
    $pattern = [Text.Encoding]::ASCII.GetBytes($needle)
    $stream = [IO.File]::OpenRead($path)
    try {
        $chunkSize = 4MB
        $buffer = New-Object byte[] ($chunkSize + $pattern.Length)
        $carry = 0
        while (($read = $stream.Read($buffer, $carry, $chunkSize)) -gt 0) {
            $length = $carry + $read
            for ($i = 0; $i -le $length - $pattern.Length; $i++) {
                $match = $true
                for ($j = 0; $j -lt $pattern.Length; $j++) {
                    if ($buffer[$i + $j] -ne $pattern[$j]) { $match = $false; break }
                }
                if ($match) { return $true }
            }
            $carry = [Math]::Min($pattern.Length - 1, $length)
            if ($carry -gt 0) { [Array]::Copy($buffer, $length - $carry, $buffer, 0, $carry) }
        }
    }
    finally { $stream.Dispose() }
    return $false
}

if (-not (Test-Path -LiteralPath $TaomArmoryPath -PathType Container)) {
    throw "TAOM armory directory not found: $TaomArmoryPath"
}

$packages = @(Get-ChildItem -LiteralPath $TaomArmoryPath -Recurse -File -Filter *.tpac -ErrorAction SilentlyContinue)
if ($packages.Count -eq 0) { throw 'No .tpac packages found.' }

$hits = [ordered]@{}
$selected = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($resource in $requiredResources) {
    $resourceHits = New-Object System.Collections.Generic.List[string]
    foreach ($package in $packages) {
        try {
            if (Test-BinaryContainsAscii $package.FullName $resource) {
                $resourceHits.Add($package.FullName)
                [void]$selected.Add($package.FullName)
            }
        } catch { }
    }
    $hits[$resource] = @($resourceHits)
}

$missing = @($requiredResources | Where-Object { @($hits[$_]).Count -eq 0 })
if ($missing.Count -gt 0) {
    $report = [ordered]@{
        status = 'BLOCKED_MISSING_BINARY_RESOURCES'
        missing = $missing
        hits = $hits
    }
    $report | ConvertTo-Json -Depth 6
    exit 2
}

New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
$packageOut = Join-Path $OutputPath 'AssetPackages_CANDIDATE_ONLY'
New-Item -ItemType Directory -Path $packageOut -Force | Out-Null

$copied = New-Object System.Collections.Generic.List[object]
foreach ($path in $selected) {
    $file = Get-Item -LiteralPath $path
    $target = Join-Path $packageOut $file.Name
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    $copied.Add([ordered]@{ source = $file.FullName; target = $target; bytes = $file.Length })
}

# Keep text definitions as evidence only. They are not automatically loaded by KaiTOR;
# race/skin/action-set definitions must be reconciled with TOR before release.
$definitionOut = Join-Path $OutputPath 'Definitions_REFERENCE_ONLY'
New-Item -ItemType Directory -Path $definitionOut -Force | Out-Null
$definitionNames = @('skins.xml','action_sets.xml','monsters.xml')
$definitions = New-Object System.Collections.Generic.List[string]
foreach ($name in $definitionNames) {
    foreach ($file in Get-ChildItem -LiteralPath $TaomArmoryPath -Recurse -File -Filter $name -ErrorAction SilentlyContinue) {
        $target = Join-Path $definitionOut $file.Name
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        $definitions.Add($file.FullName)
    }
}

$final = [ordered]@{
    status = 'CANDIDATE_PACKAGES_STAGED'
    resources = $hits
    selected_package_count = $selected.Count
    copied_packages = @($copied)
    reference_definitions = @($definitions)
    warning = 'Candidate packages can still reference materials/textures in other TAOM packages. Do not ship or load them until dependency and TOR-rig tests pass.'
}
$final | ConvertTo-Json -Depth 8

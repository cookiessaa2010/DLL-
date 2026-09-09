[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$RuntimeLogPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BeforeCommandOutputPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$AfterCommandOutputPath,

    [ValidateNotNullOrEmpty()]
    [string]$OutputDirectory = (Join-Path $PWD 'kaitor-live-acceptance')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runtimeDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exporter = Join-Path $runtimeDir 'Export-KaiTORFourPlayerSnapshot.ps1'
$combinedValidator = Join-Path $runtimeDir 'Test-KaiTORTorFourPlayerRuntime.ps1'

foreach ($required in @($exporter, $combinedValidator)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "KaiTOR live acceptance dependency missing: $required"
    }
}

foreach ($input in @(
    @{ Name = 'TOR runtime log'; Path = $RuntimeLogPath },
    @{ Name = 'before snapshot command output'; Path = $BeforeCommandOutputPath },
    @{ Name = 'after snapshot command output'; Path = $AfterCommandOutputPath }
)) {
    if (-not (Test-Path -LiteralPath $input.Path -PathType Leaf)) {
        throw "KaiTOR $($input.Name) not found: $($input.Path)"
    }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$resolvedOutput = (Resolve-Path -LiteralPath $OutputDirectory).Path
$beforeSnapshot = Join-Path $resolvedOutput 'snapshot-before.json'
$afterSnapshot = Join-Path $resolvedOutput 'snapshot-after.json'
$evidenceRuntimeLog = Join-Path $resolvedOutput 'runtime.log'
$evidenceBeforeCommand = Join-Path $resolvedOutput 'snapshot-command-before.txt'
$evidenceAfterCommand = Join-Path $resolvedOutput 'snapshot-command-after.txt'
$manifestPath = Join-Path $resolvedOutput 'acceptance.json'

function Copy-EvidenceFile {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $resolvedSource = (Resolve-Path -LiteralPath $Source).Path
    $destinationFull = [IO.Path]::GetFullPath($Destination)
    if (-not [string]::Equals($resolvedSource, $destinationFull, [StringComparison]::OrdinalIgnoreCase)) {
        Copy-Item -LiteralPath $resolvedSource -Destination $destinationFull -Force
    }
}

Copy-EvidenceFile -Source $RuntimeLogPath -Destination $evidenceRuntimeLog
Copy-EvidenceFile -Source $BeforeCommandOutputPath -Destination $evidenceBeforeCommand
Copy-EvidenceFile -Source $AfterCommandOutputPath -Destination $evidenceAfterCommand

Write-Output '=== Export authoritative snapshot before movement ==='
& $exporter -InputPath $evidenceBeforeCommand -OutputPath $beforeSnapshot -Phase four-online

Write-Output '=== Export authoritative snapshot after movement ==='
& $exporter -InputPath $evidenceAfterCommand -OutputPath $afterSnapshot -Phase four-online

Write-Output '=== Run combined TOR + four-player runtime acceptance ==='
& $combinedValidator `
    -LogPath $evidenceRuntimeLog `
    -BeforeSnapshotPath $beforeSnapshot `
    -AfterSnapshotPath $afterSnapshot

$evidenceFiles = [ordered]@{}
foreach ($entry in @(
    @{ Name = 'runtimeLog'; Path = $evidenceRuntimeLog },
    @{ Name = 'beforeCommandOutput'; Path = $evidenceBeforeCommand },
    @{ Name = 'afterCommandOutput'; Path = $evidenceAfterCommand },
    @{ Name = 'beforeSnapshot'; Path = $beforeSnapshot },
    @{ Name = 'afterSnapshot'; Path = $afterSnapshot }
)) {
    $item = Get-Item -LiteralPath $entry.Path
    $evidenceFiles[$entry.Name] = [ordered]@{
        file = $item.Name
        bytes = $item.Length
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$manifest = [ordered]@{
    schema = 'kaitor-online-live-acceptance/v1'
    result = 'PASS'
    acceptedAtUtc = [DateTime]::UtcNow.ToString('o')
    target = [ordered]@{
        bannerlord = '1.3.15.110062'
        theOldRealms = '1.3.15'
        simultaneousPlayers = 4
        campaignProcess = 'Bannerlord.exe /singleplayer /server'
    }
    evidence = $evidenceFiles
}
$manifestJson = $manifest | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText($manifestPath, $manifestJson + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))

Write-Output "KaiTOR live acceptance artifacts: $resolvedOutput"
Write-Output "KaiTOR live acceptance manifest: $manifestPath"
Write-Output 'KaiTOR Online TOR live 4-player acceptance: PASS'

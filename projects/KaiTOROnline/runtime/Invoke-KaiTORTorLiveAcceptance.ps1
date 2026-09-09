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

Write-Output '=== Export authoritative snapshot before movement ==='
& $exporter -InputPath $BeforeCommandOutputPath -OutputPath $beforeSnapshot -Phase four-online

Write-Output '=== Export authoritative snapshot after movement ==='
& $exporter -InputPath $AfterCommandOutputPath -OutputPath $afterSnapshot -Phase four-online

Write-Output '=== Run combined TOR + four-player runtime acceptance ==='
& $combinedValidator `
    -LogPath $RuntimeLogPath `
    -BeforeSnapshotPath $beforeSnapshot `
    -AfterSnapshotPath $afterSnapshot

Write-Output "KaiTOR live acceptance artifacts: $resolvedOutput"
Write-Output 'KaiTOR Online TOR live 4-player acceptance: PASS'

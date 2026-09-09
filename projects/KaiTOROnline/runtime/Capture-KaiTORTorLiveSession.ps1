[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$RuntimeLogPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$CommandOutputPath,

    [ValidateNotNullOrEmpty()]
    [string]$OutputDirectory = (Join-Path $PWD 'kaitor-live-capture'),

    [ValidateRange(1, 3600)]
    [int]$TimeoutSeconds = 300,

    [ValidateRange(100, 10000)]
    [int]$PollMilliseconds = 500
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$marker = 'KAITOR_4P_SNAPSHOT_JSON='
$runtimeDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$acceptanceRunner = Join-Path $runtimeDir 'Invoke-KaiTORTorLiveAcceptance.ps1'
if (-not (Test-Path -LiteralPath $acceptanceRunner -PathType Leaf)) {
    throw "KaiTOR live acceptance runner missing: $acceptanceRunner"
}

foreach ($input in @(
    @{ Name = 'TOR runtime log'; Path = $RuntimeLogPath },
    @{ Name = 'server command output'; Path = $CommandOutputPath }
)) {
    if (-not (Test-Path -LiteralPath $input.Path -PathType Leaf)) {
        throw "KaiTOR $($input.Name) not found: $($input.Path)"
    }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$resolvedOutput = (Resolve-Path -LiteralPath $OutputDirectory).Path
$beforeOutput = Join-Path $resolvedOutput 'snapshot-command-before.txt'
$afterOutput = Join-Path $resolvedOutput 'snapshot-command-after.txt'

function Get-SnapshotRecords {
    param([Parameter(Mandatory = $true)][string]$Path)
    return @(Get-Content -LiteralPath $Path | Where-Object { $_ -like "*$marker*" })
}

function Wait-ForNewSnapshotRecord {
    param(
        [Parameter(Mandatory = $true)][int]$ExistingCount,
        [Parameter(Mandatory = $true)][string]$Stage
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $records = @(Get-SnapshotRecords -Path $CommandOutputPath)
        if ($records.Count -gt $ExistingCount) {
            return [string]$records[-1]
        }
        Start-Sleep -Milliseconds $PollMilliseconds
    }

    throw "Timed out waiting for $Stage snapshot. Run 'coop.debug.kaitor.snapshot4p' on the authoritative server and ensure its output is appended to $CommandOutputPath."
}

$initialRecords = @(Get-SnapshotRecords -Path $CommandOutputPath)
$initialCount = $initialRecords.Count
Write-Output "Existing snapshot records: $initialCount"
Write-Output "Capture directory: $resolvedOutput"
Write-Output "BEFORE: with all four players connected and stationary, run coop.debug.kaitor.snapshot4p on the authoritative server."
$beforeRecord = Wait-ForNewSnapshotRecord -ExistingCount $initialCount -Stage 'BEFORE'
[IO.File]::WriteAllText($beforeOutput, $beforeRecord + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Output 'BEFORE snapshot captured.'

$beforeCount = @(Get-SnapshotRecords -Path $CommandOutputPath).Count
Write-Output 'Now move all four player parties independently on the campaign map.'
Write-Output 'AFTER: run coop.debug.kaitor.snapshot4p again on the authoritative server.'
$afterRecord = Wait-ForNewSnapshotRecord -ExistingCount $beforeCount -Stage 'AFTER'
[IO.File]::WriteAllText($afterOutput, $afterRecord + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Output 'AFTER snapshot captured.'

& $acceptanceRunner `
    -RuntimeLogPath $RuntimeLogPath `
    -BeforeCommandOutputPath $beforeOutput `
    -AfterCommandOutputPath $afterOutput `
    -OutputDirectory $resolvedOutput

Write-Output 'KaiTOR Online live TOR 4-player capture and acceptance: PASS'

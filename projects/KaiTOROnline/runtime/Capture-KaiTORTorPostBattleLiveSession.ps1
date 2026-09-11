[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$RuntimeLogPath,
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$CommandOutputPath,
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$ControllerId,
    [ValidateNotNullOrEmpty()][string]$OutputDirectory = (Join-Path $PWD 'kaitor-tor-post-battle-live-capture'),
    [ValidateRange(1, 3600)][int]$TimeoutSeconds = 300,
    [ValidateRange(100, 10000)][int]$PollMilliseconds = 500
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$marker = 'KAITOR_4P_SNAPSHOT_JSON='
$runtimeDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$acceptanceRunner = Join-Path $runtimeDir 'Invoke-KaiTORTorPostBattleLiveAcceptance.ps1'
if (-not (Test-Path -LiteralPath $acceptanceRunner -PathType Leaf)) {
    throw "KaiTOR TOR post-battle live acceptance runner missing: $acceptanceRunner"
}

foreach ($input in @(
    @{ Name = 'TOR runtime log'; Path = $RuntimeLogPath },
    @{ Name = 'server command output'; Path = $CommandOutputPath }
)) {
    if (-not (Test-Path -LiteralPath $input.Path -PathType Leaf)) {
        throw "KaiTOR $($input.Name) not found: $($input.Path)"
    }
}

$resolvedRuntimeLog = (Resolve-Path -LiteralPath $RuntimeLogPath).Path
$resolvedCommandOutput = (Resolve-Path -LiteralPath $CommandOutputPath).Path
$serverRunningPattern = '(?im)\bcampaign\s+server\b[^\r\n]{0,120}\b(?:entered\s+running\s+state|running|ready)\b'

function Get-BytesSha256 {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

$runtimeAnchorBytes = [IO.File]::ReadAllBytes($resolvedRuntimeLog)
if ($runtimeAnchorBytes.Length -eq 0) {
    throw "KaiTOR TOR runtime log is empty: $resolvedRuntimeLog"
}
$runtimeAnchorText = [Text.Encoding]::UTF8.GetString($runtimeAnchorBytes)
$runtimeReadyCount = [regex]::Matches($runtimeAnchorText, $serverRunningPattern).Count
if ($runtimeReadyCount -lt 1) {
    throw "KaiTOR TOR live capture requires an existing campaign server ready/running marker in $resolvedRuntimeLog"
}
$runtimeAnchorHash = Get-BytesSha256 -Bytes $runtimeAnchorBytes
$runtimeAnchorLength = $runtimeAnchorBytes.Length

$commandAnchorBytes = [IO.File]::ReadAllBytes($resolvedCommandOutput)
$commandAnchorHash = Get-BytesSha256 -Bytes $commandAnchorBytes
$commandAnchorLength = $commandAnchorBytes.Length

function Assert-ServerSessionContinuity {
    $currentBytes = [IO.File]::ReadAllBytes($resolvedRuntimeLog)
    if ($currentBytes.Length -lt $runtimeAnchorLength) {
        throw 'KaiTOR TOR live capture rejected: runtime log was truncated or replaced during capture.'
    }

    $prefixBytes = New-Object byte[] $runtimeAnchorLength
    [Array]::Copy($currentBytes, 0, $prefixBytes, 0, $runtimeAnchorLength)
    $prefixHash = Get-BytesSha256 -Bytes $prefixBytes
    if ($prefixHash -cne $runtimeAnchorHash) {
        throw 'KaiTOR TOR live capture rejected: runtime log prefix changed during capture; authoritative server continuity is not proven.'
    }

    $currentText = [Text.Encoding]::UTF8.GetString($currentBytes)
    $currentReadyCount = [regex]::Matches($currentText, $serverRunningPattern).Count
    if ($currentReadyCount -ne $runtimeReadyCount) {
        throw "KaiTOR TOR live capture rejected: campaign server ready/running marker count changed from $runtimeReadyCount to $currentReadyCount; a server restart or second session was observed."
    }
}

function Assert-CommandOutputContinuity {
    $currentBytes = [IO.File]::ReadAllBytes($resolvedCommandOutput)
    if ($currentBytes.Length -lt $commandAnchorLength) {
        throw 'KaiTOR TOR live capture rejected: server command output was truncated or replaced during capture.'
    }

    if ($commandAnchorLength -gt 0) {
        $prefixBytes = New-Object byte[] $commandAnchorLength
        [Array]::Copy($currentBytes, 0, $prefixBytes, 0, $commandAnchorLength)
        $prefixHash = Get-BytesSha256 -Bytes $prefixBytes
        if ($prefixHash -cne $commandAnchorHash) {
            throw 'KaiTOR TOR live capture rejected: server command output prefix changed during capture; snapshot stream is not append-only.'
        }
    }
}

function Assert-LiveCaptureContinuity {
    Assert-ServerSessionContinuity
    Assert-CommandOutputContinuity
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$resolvedOutput = (Resolve-Path -LiteralPath $OutputDirectory).Path
$inBattleOutput = Join-Path $resolvedOutput 'snapshot-command-in-battle.txt'
$postBattleOutput = Join-Path $resolvedOutput 'snapshot-command-post-battle.txt'
$movedOutput = Join-Path $resolvedOutput 'snapshot-command-moved.txt'

function Get-SnapshotRecords {
    param([Parameter(Mandatory = $true)][string]$Path)
    return @(Get-Content -LiteralPath $Path | Where-Object { $_ -like "*$marker*" })
}

function Wait-ForSnapshotAtIndex {
    param(
        [Parameter(Mandatory = $true)][int]$Index,
        [Parameter(Mandatory = $true)][string]$Stage
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        Assert-LiveCaptureContinuity
        $records = @(Get-SnapshotRecords -Path $resolvedCommandOutput)
        if ($records.Count -gt $Index) {
            Assert-LiveCaptureContinuity
            return [string]$records[$Index]
        }
        Start-Sleep -Milliseconds $PollMilliseconds
    }

    throw "Timed out waiting for $Stage snapshot at stream index $Index. Run 'coop.debug.kaitor.snapshot4p' on the same authoritative server session and ensure output is appended to $resolvedCommandOutput."
}

Assert-LiveCaptureContinuity
$initialRecords = @(Get-SnapshotRecords -Path $resolvedCommandOutput)
$baseIndex = $initialRecords.Count
Write-Output "Existing snapshot records: $baseIndex"
Write-Output "Capture directory: $resolvedOutput"
Write-Output "Authoritative runtime continuity anchor: readyMarkers=$runtimeReadyCount prefixBytes=$runtimeAnchorLength sha256=$runtimeAnchorHash"
Write-Output "Authoritative command stream anchor: prefixBytes=$commandAnchorLength sha256=$commandAnchorHash"
Write-Output 'IN-BATTLE: while the selected controller is in the active MapEvent, run coop.debug.kaitor.snapshot4p.'
$inBattleRecord = Wait-ForSnapshotAtIndex -Index $baseIndex -Stage 'IN-BATTLE'
[IO.File]::WriteAllText($inBattleOutput, $inBattleRecord + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Output 'IN-BATTLE snapshot captured from the authoritative output stream.'

Write-Output 'POST-BATTLE: after the MapEvent is cleared and all four parties are back on the campaign map, run coop.debug.kaitor.snapshot4p.'
$postBattleRecord = Wait-ForSnapshotAtIndex -Index ($baseIndex + 1) -Stage 'POST-BATTLE'
[IO.File]::WriteAllText($postBattleOutput, $postBattleRecord + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Output 'POST-BATTLE snapshot captured from the same authoritative output stream.'

Write-Output 'MOVED: move all four parties independently, then run coop.debug.kaitor.snapshot4p again.'
$movedRecord = Wait-ForSnapshotAtIndex -Index ($baseIndex + 2) -Stage 'MOVED'
[IO.File]::WriteAllText($movedOutput, $movedRecord + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Output 'MOVED snapshot captured from the same authoritative output stream.'

Assert-LiveCaptureContinuity
& $acceptanceRunner `
    -RuntimeLogPath $resolvedRuntimeLog `
    -InBattleCommandOutputPath $inBattleOutput `
    -PostBattleCommandOutputPath $postBattleOutput `
    -MovedCommandOutputPath $movedOutput `
    -ControllerId $ControllerId `
    -OutputDirectory $resolvedOutput

Write-Output 'KaiTOR Online TOR post-battle single-stream live capture and acceptance: PASS'

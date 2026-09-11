[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$RuntimeLogPath,
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$InBattleCommandOutputPath,
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$PostBattleCommandOutputPath,
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$MovedCommandOutputPath,
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$ControllerId,
    [ValidateNotNullOrEmpty()][string]$OutputDirectory = (Join-Path $PWD 'kaitor-tor-post-battle-acceptance')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runtimeDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exporter = Join-Path $runtimeDir 'Export-KaiTORFourPlayerSnapshot.ps1'
$validator = Join-Path $runtimeDir 'Test-KaiTORTorPostBattleRuntime.ps1'
foreach ($required in @($exporter, $validator)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "KaiTOR TOR post-battle live acceptance dependency missing: $required"
    }
}

foreach ($input in @(
    @{ Name = 'TOR runtime log'; Path = $RuntimeLogPath },
    @{ Name = 'in-battle command output'; Path = $InBattleCommandOutputPath },
    @{ Name = 'post-battle command output'; Path = $PostBattleCommandOutputPath },
    @{ Name = 'moved command output'; Path = $MovedCommandOutputPath }
)) {
    if (-not (Test-Path -LiteralPath $input.Path -PathType Leaf)) {
        throw "KaiTOR $($input.Name) not found: $($input.Path)"
    }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$resolvedOutput = (Resolve-Path -LiteralPath $OutputDirectory).Path

function Copy-EvidenceFile {
    param([string]$Source,[string]$Destination)
    $resolvedSource = (Resolve-Path -LiteralPath $Source).Path
    $destinationFull = [IO.Path]::GetFullPath($Destination)
    if (-not [string]::Equals($resolvedSource, $destinationFull, [StringComparison]::OrdinalIgnoreCase)) {
        Copy-Item -LiteralPath $resolvedSource -Destination $destinationFull -Force
    }
}

$runtimeEvidence = Join-Path $resolvedOutput 'runtime.log'
$battleCommand = Join-Path $resolvedOutput 'snapshot-command-in-battle.txt'
$postCommand = Join-Path $resolvedOutput 'snapshot-command-post-battle.txt'
$movedCommand = Join-Path $resolvedOutput 'snapshot-command-moved.txt'
$battleSnapshot = Join-Path $resolvedOutput 'snapshot-in-battle.json'
$postSnapshot = Join-Path $resolvedOutput 'snapshot-post-battle.json'
$movedSnapshot = Join-Path $resolvedOutput 'snapshot-moved.json'
$manifestPath = Join-Path $resolvedOutput 'acceptance-post-battle.json'

Copy-EvidenceFile $RuntimeLogPath $runtimeEvidence
Copy-EvidenceFile $InBattleCommandOutputPath $battleCommand
Copy-EvidenceFile $PostBattleCommandOutputPath $postCommand
Copy-EvidenceFile $MovedCommandOutputPath $movedCommand

& $exporter -InputPath $battleCommand -OutputPath $battleSnapshot -Phase four-online
& $exporter -InputPath $postCommand -OutputPath $postSnapshot -Phase four-online
& $exporter -InputPath $movedCommand -OutputPath $movedSnapshot -Phase four-online

& $validator `
    -LogPath $runtimeEvidence `
    -InBattleSnapshotPath $battleSnapshot `
    -PostBattleSnapshotPath $postSnapshot `
    -MovedSnapshotPath $movedSnapshot `
    -ControllerId $ControllerId

$evidenceFiles = [ordered]@{}
foreach ($entry in @(
    @{ Name = 'runtimeLog'; Path = $runtimeEvidence },
    @{ Name = 'inBattleCommandOutput'; Path = $battleCommand },
    @{ Name = 'postBattleCommandOutput'; Path = $postCommand },
    @{ Name = 'movedCommandOutput'; Path = $movedCommand },
    @{ Name = 'inBattleSnapshot'; Path = $battleSnapshot },
    @{ Name = 'postBattleSnapshot'; Path = $postSnapshot },
    @{ Name = 'movedSnapshot'; Path = $movedSnapshot }
)) {
    $item = Get-Item -LiteralPath $entry.Path
    $evidenceFiles[$entry.Name] = [ordered]@{
        file = $item.Name
        bytes = $item.Length
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$manifest = [ordered]@{
    schema = 'kaitor-online-tor-post-battle-live-acceptance/v1'
    result = 'PASS'
    acceptedAtUtc = [DateTime]::UtcNow.ToString('o')
    target = [ordered]@{
        bannerlord = '1.3.15.110062'
        theOldRealms = '1.3.15'
        simultaneousPlayers = 4
        campaignProcess = 'Bannerlord.exe /singleplayer /server'
        controllerId = $ControllerId
    }
    evidence = $evidenceFiles
}
[IO.File]::WriteAllText($manifestPath, (($manifest | ConvertTo-Json -Depth 8) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))

Write-Output "KaiTOR TOR post-battle live acceptance artifacts: $resolvedOutput"
Write-Output "KaiTOR TOR post-battle live acceptance manifest: $manifestPath"
Write-Output 'KaiTOR Online TOR post-battle live 4-player acceptance: PASS'

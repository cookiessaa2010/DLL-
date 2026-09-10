param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'

function Replace-Exact {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Old,
        [Parameter(Mandatory = $true)][string]$New
    )

    $fullPath = Join-Path $UpstreamRoot $Path
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "Missing upstream file: $Path"
    }

    $text = [IO.File]::ReadAllText($fullPath) -replace "`r`n", "`n"
    $oldNormalized = $Old -replace "`r`n", "`n"
    $newNormalized = $New -replace "`r`n", "`n"
    if (-not $text.Contains($oldNormalized)) {
        throw "Anchor not found in $Path`n--- anchor ---`n$oldNormalized"
    }

    [IO.File]::WriteAllText(
        $fullPath,
        $text.Replace($oldNormalized, $newNormalized),
        [Text.UTF8Encoding]::new($false))
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$overlay = Join-Path $projectRoot 'upstream-overlay/source/Coop.Core/Server/Commands/KaiTORFourPlayerSnapshotCommand.cs'
$target = Join-Path $UpstreamRoot 'source/Coop.Core/Server/Commands/KaiTORFourPlayerSnapshotCommand.cs'

if (-not (Test-Path -LiteralPath $overlay)) {
    throw "Missing KaiTOR shared-map diagnostics overlay: $overlay"
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
Copy-Item -LiteralPath $overlay -Destination $target -Force

Replace-Exact `
    'source/Coop.Core/Server/ServerModule.cs' `
    'using Coop.Core.Server.Connections;' `
    "using Coop.Core.Server.Connections;`nusing Coop.Core.Server.Commands;"

Replace-Exact `
    'source/Coop.Core/Server/ServerModule.cs' `
    '        builder.RegisterModule<ConnectionModule>();' `
    "        builder.RegisterModule<ConnectionModule>();`n        builder.RegisterType<KaiTORFourPlayerSnapshotCommand>().As<ICoopCommand>().InstancePerDependency();"

& (Join-Path $PSScriptRoot 'ApplyFourPlayerMovementOwnership.ps1') -UpstreamRoot $UpstreamRoot
& (Join-Path $PSScriptRoot 'ApplyFourPlayerEncounterOwnership.ps1') -UpstreamRoot $UpstreamRoot
& (Join-Path $PSScriptRoot 'ApplyFourPlayerMissionFinalizeOwnership.ps1') -UpstreamRoot $UpstreamRoot
& (Join-Path $PSScriptRoot 'ApplyFourPlayerBattleLeaveOwnership.ps1') -UpstreamRoot $UpstreamRoot
& (Join-Path $PSScriptRoot 'ApplyFourPlayerTimeAuthority.ps1') -UpstreamRoot $UpstreamRoot

# Defeat lifecycle: never let reconnect/restart implicitly resurrect a permanently dead Hero by
# manufacturing a fresh active MobileParty. Prisoners remain parked and released/live heroes can
# regain authoritative leadership through the existing restorer.
& (Join-Path $PSScriptRoot 'ApplyFourPlayerDefeatLifecycleSafety.ps1') -UpstreamRoot $UpstreamRoot

& (Join-Path $PSScriptRoot 'TestFourPlayerMissionIsolationStaging.ps1') -UpstreamRoot $UpstreamRoot
& (Join-Path $PSScriptRoot 'TestFourPlayerTimeAuthorityStaging.ps1') -UpstreamRoot $UpstreamRoot
& (Join-Path $PSScriptRoot 'TestFourPlayerDefeatLifecycleStaging.ps1') -UpstreamRoot $UpstreamRoot

Write-Host 'KaiTOR shared-map diagnostics applied successfully.'
Write-Host 'Server command: coop.debug.kaitor.snapshot4p'

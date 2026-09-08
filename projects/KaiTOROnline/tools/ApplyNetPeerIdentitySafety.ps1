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
    if (-not (Test-Path $fullPath)) {
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

# LiteNetLib.NetPeer ultimately derives from IPEndPoint, so its inherited value equality is endpoint-based.
# Reconnect bookkeeping must distinguish the old NetPeer object from the replacement NetPeer even when both
# represent the same remote endpoint. The upstream tree already provides Common.Network.ReferenceComparer<T>
# and uses it in several hot-path peer maps; apply the same rule to the remaining authoritative registries
# that own connection/player/mission membership.

Replace-Exact `
    'source/Coop.Core/Server/Connections/ConnectionCollection.cs' `
    "using Common.Messaging;`nusing Common.Network.Messages;" `
    "using Common.Messaging;`nusing Common.Network;`nusing Common.Network.Messages;"

Replace-Exact `
    'source/Coop.Core/Server/Connections/ConnectionCollection.cs' `
    '    public ConcurrentDictionary<NetPeer, IConnectionLogic> ConnectionStates { get; private set; } = new();' `
    '    public ConcurrentDictionary<NetPeer, IConnectionLogic> ConnectionStates { get; private set; } = new(ReferenceComparer<NetPeer>.Instance);'

Replace-Exact `
    'source/GameInterface/Services/Players/PlayerManager.cs' `
    "using Common;`nusing GameInterface.Services.Entity;" `
    "using Common;`nusing Common.Network;`nusing GameInterface.Services.Entity;"

Replace-Exact `
    'source/GameInterface/Services/Players/PlayerManager.cs' `
    '    private readonly ConcurrentDictionary<NetPeer, Player> peerToPlayer = new();' `
    '    private readonly ConcurrentDictionary<NetPeer, Player> peerToPlayer = new(ReferenceComparer<NetPeer>.Instance);'

# Mission membership is not part of the 0.0.1 map bootstrap, but it is an authoritative server registry
# and has the same stale-disconnect/reconnect failure mode. Fix it now while the invariant is explicit.
Replace-Exact `
    'source/Coop.Core/Server/Services/Instances/MissionManager.cs' `
    "using Common.Logging;`nusing Common.Network.Data;" `
    "using Common.Logging;`nusing Common.Network;`nusing Common.Network.Data;"

Replace-Exact `
    'source/Coop.Core/Server/Services/Instances/MissionManager.cs' `
    '    private readonly Dictionary<NetPeer, MissionMembership> byPeer = new Dictionary<NetPeer, MissionMembership>();' `
    '    private readonly Dictionary<NetPeer, MissionMembership> byPeer = new Dictionary<NetPeer, MissionMembership>(ReferenceComparer<NetPeer>.Instance);'

Replace-Exact `
    'source/Coop.Core/Server/Services/Instances/MissionManager.cs' `
    '    private readonly Dictionary<NetPeer, long> relayRevocationCounts = new Dictionary<NetPeer, long>();' `
    '    private readonly Dictionary<NetPeer, long> relayRevocationCounts = new Dictionary<NetPeer, long>(ReferenceComparer<NetPeer>.Instance);'

Write-Host 'Applied reference-identity semantics to authoritative NetPeer registries.'

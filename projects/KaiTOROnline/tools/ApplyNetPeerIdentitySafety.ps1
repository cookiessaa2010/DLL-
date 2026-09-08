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

# The pinned Coop commit predates Common.Network.ReferenceComparer<T>, while newer upstream
# already uses that helper for peer-keyed registries. Backport the tiny identity comparer into
# the staged checkout so reconnect hardening can use one consistent comparer across projects.
$referenceComparerPath = Join-Path $UpstreamRoot 'source/Common/Network/ReferenceComparer.cs'
if (-not (Test-Path $referenceComparerPath)) {
    $referenceComparerDirectory = Split-Path -Parent $referenceComparerPath
    New-Item -ItemType Directory -Force -Path $referenceComparerDirectory | Out-Null

    $referenceComparerSource = @'
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Common.Network
{
    /// <summary>Compares reference types strictly by object identity.</summary>
    public sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();

        private ReferenceComparer()
        {
        }

        public bool Equals(T x, T y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => obj == null ? 0 : RuntimeHelpers.GetHashCode(obj);
    }
}
'@

    [IO.File]::WriteAllText(
        $referenceComparerPath,
        $referenceComparerSource,
        [Text.UTF8Encoding]::new($false))

    Write-Host 'Backported Common.Network.ReferenceComparer<T> into pinned Coop source.'
}
else {
    Write-Host 'Pinned Coop source already provides Common.Network.ReferenceComparer<T>; reusing it.'
}

# LiteNetLib.NetPeer ultimately derives from IPEndPoint, so inherited value equality can collapse
# two different peer objects that represent the same endpoint. Reconnect bookkeeping must instead
# distinguish the old NetPeer object from its replacement. Apply reference identity to authoritative
# registries that own connection/player/mission membership.

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

# Join catch-up bookkeeping also lives across the reconnect boundary. A replacement peer must not
# inherit the old peer's catch-up start timestamp, nor be considered the same member of the active set.
Replace-Exact `
    'source/Coop.Core/Server/Services/Time/OverloadedPeerManager.cs' `
    '    private readonly Dictionary<NetPeer, DateTime> joinCatchUpStartedUtc = new Dictionary<NetPeer, DateTime>();' `
    '    private readonly Dictionary<NetPeer, DateTime> joinCatchUpStartedUtc = new Dictionary<NetPeer, DateTime>(ReferenceComparer<NetPeer>.Instance);'

Replace-Exact `
    'source/Coop.Core/Server/Services/Time/OverloadedPeerManager.cs' `
    '        var activePeers = new HashSet<NetPeer>(peers);' `
    '        var activePeers = new HashSet<NetPeer>(peers, ReferenceComparer<NetPeer>.Instance);'

Write-Host 'Applied reference-identity semantics to authoritative NetPeer registries and join catch-up tracking.'

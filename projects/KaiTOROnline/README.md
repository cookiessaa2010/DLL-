# KaiTOR Online

Experimental The Old Realms multiplayer compatibility project for **Mount & Blade II: Bannerlord v1.3.15.110062**.

## Fixed project target

- Bannerlord: **v1.3.15.110062**
- The Old Realms: **v1.3.15**
- Players: **4 simultaneous players maximum**
- Topology: **dedicated authoritative server**
- Every connected player owns a full campaign Hero + MobileParty and can act independently on the shared campaign map.
- Source base: Bannerlord Coop, used under permission granted by its maintainers. Preserve the permission record with the project before public distribution.

## Current development status

Completed foundation work:

- dedicated `kaitor-online-4p` development branch
- backport audit for Bannerlord 1.3.15.110062
- concurrency-safe server admission gate with a hard `MaxPlayers = 4`
- persistent-controller reconnect semantics: reconnect replaces the old network session instead of consuming a fifth slot
- disconnect releases only the live session slot, not persistent Hero/Party data
- explicit design for a visible `ServerFull` validation rejection before character creation/save transfer
- .NET Framework 4.7.2 test project
- CI coverage for 1..4 joins, rejected fifth join, reconnect, disconnect/reuse, stale disconnect, idempotent validation, invalid identity, and 32-way concurrent join stress
- CI currently passing
- staged upstream patch touching the confirmed Coop connection flow (`ResolveCharacterState`, `ConnectionContext`, `ConnectionCollection`, validation message/client state)

The next blocker is the **Bannerlord Coop -> Bannerlord 1.3.15 API backport**, not the four-player capacity logic.

## 0.0.1 prototype acceptance criteria

The first prototype is intentionally small. It is successful when:

1. A dedicated server boots against Bannerlord 1.3.15 references.
2. Four clients can connect; a fifth connection is rejected cleanly.
3. All four players have distinct player identities, Hero state and MobileParty state.
4. All four parties can move independently on one campaign map.
5. AI campaign parties continue to update while players move.
6. Server time is authoritative and clients converge on the same campaign state.
7. Server save/restart restores all four players and their last authoritative positions.
8. No TOR combat/magic synchronization is required for this milestone.

## Development order

1. Backport Bannerlord Coop build/runtime layer from current supported Bannerlord to 1.3.15.
2. Enforce a hard 4-player admission cap at the server boundary.
3. Validate shared-map synchronization in vanilla 1.3.15.
4. Load TOR modules and catalog failures.
5. Add a separate TOR compatibility layer rather than scattering TOR conditionals through the networking core.
6. Sync TOR campaign objects first.
7. Add autoresolve encounters.
8. Add real battles.
9. Add TOR troops/monsters.
10. Add server-authoritative TOR magic/careers.

## Non-goals for 0.0.1

- More than four players
- TOR spells
- TOR battle synchronization
- Sieges
- Quests
- Full campaign feature parity
- Public release

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

Completed foundation and backport work:

- dedicated `kaitor-online-4p` development branch
- Bannerlord Coop source pinned for reproducible work at `107d1c29ec3dc2d9f2d3a7836ae1691c2391e00c`
- Bannerlord **1.3.15.110062** reference staging and compile backport
- `Coop.Core` builds successfully against the staged 1.3.15 reference graph
- full `Coop` module builds successfully against 1.3.15
- server-console dependency graph builds successfully
- server-console startup + clean-shutdown smoke test passes in CI
- generated module metadata is locked and validated at Bannerlord `v1.3.15`
- distributable bootstrap packaging strips Bannerlord/TaleWorlds game assemblies and audits managed dependency versions
- concurrency-safe server admission gate with a hard `MaxPlayers = 4`
- 1..4 joins accepted; fifth distinct controller receives a visible server-full rejection before character creation
- persistent-controller reconnect semantics: reconnect replaces the old network session instead of consuming a fifth slot
- disconnect releases only the live admission slot, not persistent Hero/Clan/MobileParty registration
- CI coverage for reconnect, disconnect/reuse, stale disconnect, idempotent validation, invalid identity, and 32-way concurrent join stress
- connection-generation safety hardened for peer-keyed server registries using **reference identity**, including connection state, player-peer mapping, mission membership and join catch-up tracking
- the pinned source's loading message queue already isolates `NetPeer` generations by reference identity
- persistent four-player save/load/reconnect architecture audited in `FOUR_PLAYER_PERSISTENCE_AUDIT.md`
- full backport/bootstrap pipeline is currently green; latest verified bootstrap source commit: `6d3899284c2462b6768e8d771d6bafb321c02a1e`

The build/API backport is **no longer the current blocker**. The next blocker is the first real **Bannerlord 1.3.15 four-client shared-campaign runtime test**: four distinct clients in the actual campaign process, independent map movement, fifth-player rejection, reconnect, and save/restart restoration.

## 0.0.1 prototype acceptance criteria

The first prototype is intentionally small. Current CI status is shown in parentheses; runtime-only items remain unclaimed until tested in Bannerlord itself.

1. Dedicated/server bootstrap builds and reaches listening startup against the 1.3.15 backport. **CI PASS**
2. Hard four-player admission semantics and fifth-player rejection. **isolated CI PASS; in-game runtime pending**
3. All four clients have distinct ControllerId -> Hero -> Clan -> MobileParty graphs. **architecture audited; runtime pending**
4. All four parties move independently on one campaign map. **runtime pending**
5. AI campaign parties continue to update while players move. **runtime pending**
6. Server time is authoritative and clients converge on the same campaign state. **runtime pending**
7. Server save/restart restores all four registrations and their authoritative player graphs. **save/reconnect path audited; runtime pending**
8. No TOR combat/magic synchronization is required for this milestone. **intentional**

## Four-player persistence model

KaiTOR deliberately separates **live connection capacity** from **persistent campaign identity**:

```text
ControllerId
   -> persistent Player registration
      -> HeroId
      -> ClanId
      -> MobilePartyId
      -> CharacterObjectId

ControllerId
   -> current NetPeer               (live connection only)

PlayerAdmissionGate
   -> at most 4 current controllers (live capacity only)
```

The server Coop session saves `PlayerManager.Players`. After a campaign reload, saved registrations are restored only after Bannerlord game objects have been registered. Reconnect resolves the controller back to the existing Hero/Party graph and associates the new network peer rather than creating another character.

See `FOUR_PLAYER_PERSISTENCE_AUDIT.md` for the exact save/restart/reconnect acceptance matrix.

## Runtime diagnostics already available

Pinned Coop already exposes useful commands for the first live test:

- `coop.debug.players.list` — lists every registered player and whether the Hero/Party/Clan ids resolve as controlled objects.
- `coop.debug.players.party_state <controller_id>` — server-side party identity/connectivity/activity diagnostics plus structured test output.

These should be captured before and after disconnect/reconnect and before/after server restart.

## Development order

1. ~~Backport Bannerlord Coop build/bootstrap layer to Bannerlord 1.3.15.~~ **done in CI**
2. ~~Enforce hard four-player admission at the authenticated server boundary.~~ **done/tested**
3. **Validate four independent clients on the real vanilla 1.3.15 shared campaign map.**
4. **Validate persistence: save -> full server stop -> restart -> reconnect A/B/C/D in a different order.**
5. Load TOR modules and catalog runtime failures.
6. Add a separate TOR compatibility layer rather than scattering TOR conditionals through the networking core.
7. Sync TOR campaign objects first.
8. Add autoresolve encounters.
9. Add real battles / mission instances.
10. Add TOR troops/monsters.
11. Add server-authoritative TOR magic/careers.

## Non-goals for 0.0.1

- More than four players
- TOR spells
- TOR battle synchronization
- Sieges
- Quests
- Full campaign feature parity
- Public release

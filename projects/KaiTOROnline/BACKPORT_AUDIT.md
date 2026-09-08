# Bannerlord Coop -> Bannerlord 1.3.15 backport audit

Status: initial audit

## Confirmed upstream structure

The current Bannerlord Coop development tree is already split into useful layers such as:

- `source/Common`
- `source/Coop.Core`
- `source/Coop`
- `source/GameInterface`
- integration / E2E tests
- Steam/network support

The main `Coop` project targets **.NET Framework 4.7.2**, which is compatible with the runtime target we already use for Bannerlord 1.3.15 mod work. The backport risk is therefore primarily **TaleWorlds API drift and behavior drift**, not the CLR target.

The upstream project currently references TaleWorlds assemblies from a local `mb2` game tree. For our branch, the reference tree must be pinned to an exact **1.3.15.110062** install/reference set.

## Backport workstreams

### P0 - boot/build

- Pin all TaleWorlds references to Bannerlord 1.3.15.110062.
- Find APIs/classes introduced after 1.3.15.
- Find methods whose signatures changed between 1.3.15 and the current Coop target.
- Find Harmony patches whose target method no longer matches 1.3.15.
- Get `Common`, `Coop.Core`, networking, then `Coop` compiling in that order.
- Boot a dedicated server before enabling TOR.

### P0 - 4 player admission cap

The project target is not “optimized for four”; it is **hard-limited to four simultaneous player sessions**.

Admission requirements:

- `MaxPlayers = 4` is server-owned.
- Slot count is checked before campaign player creation.
- Reconnecting the same authenticated player must not consume two slots.
- A fifth distinct player receives an explicit `ServerFull` rejection instead of connecting and failing later.
- Disconnection frees the live session slot without deleting persistent player data.
- Tests must cover 0 -> 4 joins, rejected 5th join, disconnect/rejoin, and server restart.

Do not enforce this only in UI. The authoritative gate must live in the server connection/admission path.

### P0 - campaign identity

Verify all current Coop mechanisms that virtualize or replace single-player assumptions around:

- `Hero.MainHero`
- `MobileParty.MainParty`
- player clan ownership
- player gold/inventory
- current settlement
- encounter state
- map position

Any backport change that accidentally collapses four remote players back onto one `MainHero/MainParty` context is a blocker.

### P0 - map synchronization

Validate under four concurrent players:

- independent pathing and map movement
- map event ownership
- AI party movement
- campaign tick/time
- settlement entry/exit
- player disconnect while moving
- reconnect after map movement
- save/reload positions

### P1 - TOR boot compatibility

After vanilla 1.3.15 multiplayer-map stability:

1. Enable TOR data/content modules.
2. Enable `TOR_Core` last among TOR dependencies.
3. Record every startup exception and failed Harmony patch.
4. Classify each failure as:
   - object registration
   - campaign behavior
   - model replacement
   - UI
   - mission/combat
   - save serialization
5. Fix only campaign-map blockers first.

### P2 - TOR campaign sync

Before combat, support the TOR-specific state that affects the shared campaign world. Exact types are to be discovered from TOR source/runtime audit.

### P3 - combat

Only after campaign state is stable:

- autoresolve
- field battle instances
- ordinary TOR units
- special creatures
- active abilities
- spell projectiles and AOE
- buffs/debuffs/summons

All TOR combat actions that affect authoritative state must resolve on the server.

## Four-player performance test matrix

Minimum soak tests before adding TOR battle networking:

- 4 players in four distant map regions for 60 minutes.
- 4 players entering/leaving different settlements repeatedly.
- 2 + 2 players converging on separate map encounters.
- one disconnect/reconnect every 10 minutes.
- server autosave under four active players.
- full server restart and restore.

Target: no divergent party positions, duplicated heroes/parties, duplicated inventory, or save corruption.

## First concrete code task

Locate the current Coop server's earliest authenticated admission point and add a server-side four-player gate with tests. In parallel, pin/build against 1.3.15 references and produce a compile-error inventory.

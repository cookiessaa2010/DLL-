# KaiTOR Online — four-player persistence audit

Target: Mount & Blade II: Bannerlord `1.3.15.110062`, dedicated authoritative world, hard maximum of four simultaneous players.

Pinned upstream audit base: `Bannerlord-Coop-Team/BannerlordCoop@107d1c29ec3dc2d9f2d3a7836ae1691c2391e00c`.

## Result

The pinned Coop architecture already has the correct persistence shape for four independent campaign players. KaiTOR does **not** need a second player database for the first shared-map milestone. The required work is to preserve and harden the existing controller -> Player -> Hero/Clan/MobileParty graph while backporting it to Bannerlord 1.3.15.

The hard four-player admission gate is intentionally separate from persistent registrations:

- `PlayerAdmissionGate` owns **live connection slots** only.
- `IPlayerManager` owns **persistent player registrations** for the loaded campaign.
- Disconnecting frees a live slot but must not remove the player's persistent Hero/Clan/MobileParty registration.
- Reconnecting the same controller replaces its live `NetPeer` without allocating another slot.

## Persistent player identity

`Player` stores the stable graph ids needed to restore an independent campaign player:

- controller id
- hero id
- mobile party id
- clan id
- character object id

`PlayerManager` is keyed by controller id, so one controller resolves to at most one persistent `Player` registration. It separately tracks the current controller -> `NetPeer` association. This is the correct split for reconnects.

Player-controlled game objects are marked independently of `Hero.MainHero` / `MobileParty.MainParty`, allowing all four parties to coexist in the same authoritative campaign.

## Save path

`SaveGameHandler.Handle_GameSaved` creates the Coop session from:

```text
playerRegistry.Players.ToArray()
```

Therefore every registered campaign player is written to the Coop session, not merely the currently connected player and not merely Bannerlord's vanilla MainHero.

The game save contains the actual Bannerlord Hero/Clan/MobileParty objects. The Coop session stores the controller-to-object-id mapping that reconnects an account to those objects.

## Server restart / load path

After the Bannerlord campaign is loaded and all game objects are registered, `SaveGameHandler.Handle_AllGameObjectsRegistered` restores the saved player registrations.

For old/bad saves carrying duplicate entries for one controller, upstream already selects one registration per controller and prefers the registration whose hero + party graph still exists.

Each saved registration is then passed through `IPlayerPartyRestorer.TryRestore(...)` before `PlayerManager.AddPlayer(...)`. This is important because a player's party may need repair/rebinding after load.

## Reconnect path

`ResolveCharacterState` first resolves a controller against `PlayerManager`.

For an existing controller it:

1. verifies that the registered hero still exists;
2. runs `IPlayerPartyRestorer.TryRestore` on the game thread;
3. replaces the registration if the party graph changed during restoration;
4. associates the new `NetPeer` with the existing controller;
5. tells the client that its hero already exists;
6. transfers the authoritative server save.

If a stale registration points at a hero that no longer exists, it is removed before character creation, preventing a second character from being added beside a dead registration.

## Existing players delivered to a joiner

After the save snapshot has been taken, `ExistingPlayerSender` iterates every registered player and sends the joining client each other player registration.

The joiner's own registration is skipped because it is established through its own load path. The server-host controller is also skipped.

On the client, `RemotePlayerHeroHandler` handles two cases:

- a player that joined later may carry a hero blob which is unpacked before registration;
- a player already present in the transferred save carries only ids, because its graph already exists locally after loading the save.

This means client A can know B/C/D as player-controlled Hero/Clan/MobileParty graphs without replacing A's local `MainHero`/`MainParty` identity.

## NetPeer generation safety

Reconnect correctness depends on treating two `NetPeer` objects from different connection generations as different keys even when LiteNetLib reports the same endpoint value.

KaiTOR's staged `ApplyNetPeerIdentitySafety.ps1` therefore backports a reference comparer into the pinned source and applies reference identity to:

- `ConnectionCollection.ConnectionStates`
- `PlayerManager.peerToPlayer`
- `MissionManager` peer membership/revocation dictionaries
- `OverloadedPeerManager` join catch-up timestamps and active-peer set

The pinned `ConnectionMessageQueue` already carries its own `NetPeerReferenceComparer`, so its loading/replay channels are already connection-generation safe.

## Four-player persistence acceptance test

The first real runtime test must use four distinct controller identities A/B/C/D and verify the following sequence:

1. Start a fresh dedicated world.
2. A creates Hero/Clan/Party A.
3. B creates Hero/Clan/Party B.
4. C creates Hero/Clan/Party C.
5. D creates Hero/Clan/Party D.
6. Verify all four clients see four distinct player-controlled parties on the campaign map.
7. Verify a fifth distinct controller is rejected as `Server Full` before character creation.
8. Save the server world.
9. Stop the dedicated server completely.
10. Restart from that save.
11. Reconnect in a different order, e.g. D -> B -> A -> C.
12. Verify each controller receives its original Hero/Clan/Party ids and state.
13. Disconnect B and reconnect B repeatedly; active player count remains four and no duplicate registration appears.
14. Disconnect C; a fifth distinct controller may now consume the free **live** slot, but C's persistent registration remains in the campaign save unless character deletion is explicitly requested.
15. Reconnect C after a slot is available; C must recover the original graph rather than enter character creation.

For every stage record:

- controller id
- connection/peer generation
- HeroId
- MobilePartyId
- ClanId
- CharacterObjectId
- active admission slot count
- `PlayerManager.Players` count

## Remaining risk before TOR

The persistence architecture itself is suitable. The remaining blocker is runtime validation of the 1.3.15 backport inside the actual Bannerlord campaign process. CI currently proves compilation, server-console bootstrap, protocol packaging and isolated four-slot semantics; it does not yet prove four rendered campaign clients moving independently in the 1.3.15 world.

TOR integration should begin only after this vanilla four-player save/restart/reconnect matrix passes, because TOR adds more state to each player's graph (magic resources, spells, careers, transformations and other custom systems) and would otherwise hide core campaign synchronization defects.

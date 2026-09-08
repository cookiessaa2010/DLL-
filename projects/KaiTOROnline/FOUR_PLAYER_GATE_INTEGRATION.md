# Four-player gate integration into Bannerlord Coop

Status: implementation design pinned to current upstream `development` connection flow.

## Upstream connection flow confirmed

Current Coop creates one `ConnectionLogic` per `NetPeer` and starts it in `ResolveCharacterState`.
`ResolveCharacterState.Handle_ClientValidate` is the first point where the server has both:

- the concrete joining `NetPeer`, and
- the persistent controller/player identity (`NetworkClientValidate.PlayerId`).

This is the correct authenticated admission boundary for KaiTOR Online's hard 4-player cap.

`ConnectionCollection.PlayerDisconnectedHandler` is the authoritative teardown point for live
connections and is therefore the matching place to release a reserved slot.

## Required upstream changes

### 1. Register one lifetime-scoped admission gate

Add an `IPlayerAdmissionGate` / `PlayerAdmissionGate` service to `ConnectionModule` as
`InstancePerLifetimeScope` so every connection state shares the same four-slot table.

The gate must be injected through `ConnectionContext` into every `ResolveCharacterState`.

Fixed constant:

```text
MaxPlayers = 4
```

This is not a user-facing slider for the first KaiTOR Online release.

### 2. Reserve after controller identity validation, before character resolution

In `ResolveCharacterState.Handle_ClientValidate`, after the Steam-ban check and before
`ResolveCharacter(...)`:

1. Call `admissionGate.TryReserve(controllerId, connectionId)`.
2. On `ServerFull`, send a machine-readable/visible rejection and do not enter character creation,
   save transfer, or campaign state.
3. If the same controller reconnects, replace the old live connection reservation without
   consuming another slot.
4. Disconnect the superseded old peer after the replacement reservation is committed.

The gate must be atomic. Counting `playerManager.Players` or `ConnectionStates.Count` is not enough:
multiple simultaneous joins can otherwise all observe an available fourth slot before any of them
finishes character creation.

### 3. Clean rejection protocol

Do not encode `ServerFull` as `NetworkClientValidated(false, null)`: that currently means
"no existing hero" and sends the client toward character creation.

Preferred patch:

- introduce `NetworkClientRejected` with a stable reason enum and display string; or
- extend validation with an explicit rejection field and update the client state before enabling
  the server gate.

Required reason for this project:

```text
ServerFull = "KaiTOR Online server is full (4/4 players)."
```

The client must return to the connect/menu state instead of timing out on validation.

### 4. Release on disconnect

`ConnectionCollection.PlayerDisconnectedHandler` must call the shared gate with the disconnected
peer/connection identity.

A stale superseded connection disconnecting after a same-controller reconnect must NOT release the
new session's reservation.

Persistent `Player`, Hero, Clan and MobileParty data stay registered; only the live admission slot
is released.

## Test set before TOR work

### Unit

- players 1..4 accepted
- fifth distinct controller rejected as ServerFull
- reconnect of controller 1 while 4/4 replaces its old session and remains 4/4
- disconnect frees exactly one slot
- stale old peer disconnect after reconnect does not free the replacement slot
- repeated validation packet is idempotent
- invalid/empty identity consumes no slot
- parallel join stress never produces ActiveSlots > 4

### Integration

- four rendered clients reach CampaignState
- fifth rendered client gets visible ServerFull rejection
- disconnect player 3, fifth client can then join
- reconnect player 3 to its own persistent character
- restart dedicated server and repeat

## Backport order around the gate

The gate itself is BCL-only and does not depend on TaleWorlds 1.4.x APIs, so keep it stable while
backporting Coop to Bannerlord 1.3.15.110062.

Recommended sequence:

1. Get `Common` + `Coop.Core` building against the pinned 1.3.15 reference tree.
2. Wire the admission gate and tests.
3. Get the client validation/menu path building and display ServerFull.
4. Boot vanilla 1.3.15 dedicated server.
5. Verify 4-player campaign-map join.
6. Only then add TOR modules.

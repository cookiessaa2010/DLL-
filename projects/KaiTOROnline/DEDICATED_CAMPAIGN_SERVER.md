# KaiTOR Online — dedicated campaign-server topology

Target: Bannerlord `1.3.15.110062`, hard maximum four live players.

Pinned source audited here: `Bannerlord-Coop-Team/BannerlordCoop@107d1c29ec3dc2d9f2d3a7836ae1691c2391e00c`.

## Important distinction: ServerConsole is not the campaign world

The packaged `ServerConsole` is the Coop rendezvous / listing / NAT-introduction service. Its successful startup proves that the auxiliary networking service builds and listens; it does **not** prove that the authoritative Bannerlord campaign simulation is running.

The authoritative campaign server is a separate **Bannerlord.exe** process running the Coop module in `/server` mode.

This distinction matters for KaiTOR's milestones:

```text
optional ServerConsole
    -> rendezvous / public listing / NAT introduction

Bannerlord.exe /server
    -> authoritative campaign world
    -> loads save
    -> owns campaign time + world objects
    -> runs Coop server network on UDP 4200 by default

Bannerlord.exe client A/B/C/D
    -> each owns one local MainHero/MainParty presentation
    -> sends intents to the authoritative campaign server
```

## What the pinned source already implements

### 1. The normal Host flow launches a second Bannerlord process

`ServerProcessManager.Start(...)` resolves `Bannerlord.exe` from the running game's bin directory, captures the currently active module ids, builds a fresh command line, and starts another process.

The child command line is built as:

```text
/singleplayer
/server
_MODULES_*<active module 1>*<active module 2>*...*_MODULES_
/coopsave <save name>
/coopowner <host process id>
/coopvisibility <public|friends_only|none>
[/cooppassword <password>]
```

The hosting client then connects to the spawned server over loopback. The child is deliberately independent: the launcher observes it but does not terminate it when the client leaves.

### 2. `/coopsave` is enough to auto-start a manually launched server

`ManagedServerConfig.HasAutoLoadSave` is true whenever `/coopsave` was parsed. `OwnerProcessId` only decides whether the process is labelled as a managed server.

`CoopMod.OnApplicationTick` waits until Bannerlord reaches `InitialState`, then calls:

```text
Coop.StartAsServer(saveName, password, visibility)
```

So a manually launched process can use `/server /coopsave ...` without `/coopowner` and still auto-start the Coop server + load the save.

### 3. `StartAsServer` is the authoritative campaign path

`CoopartiveMultiplayerExperience.StartAsServerCore(...)`:

1. marks the process as server;
2. creates the `ServerModule` + `GameInterfaceModule` container;
3. binds the Coop server UDP port (default `4200`);
4. applies Harmony/GameInterface patches;
5. starts server logic;
6. calls `IGameStateInterface.LoadGame(saveName)` when a save was supplied.

That is the path KaiTOR must validate in the actual Bannerlord 1.3.15 process.

## Is it truly headless?

Do **not** claim native no-render/headless support yet.

The generated Coop `SubModule.xml` currently contains:

```xml
<Tag key="DedicatedServerType" value="none" />
<Tag key="IsNoRenderModeElement" value="false" />
```

The source contains a branch for a server with no loading-screen UI, but the module metadata does not yet establish that Bannerlord 1.3.15 can run this campaign server in a native no-render process.

Therefore the verified description for 0.0.1 is:

> **standalone authoritative Bannerlord campaign-server process**

not:

> **verified native headless/no-render dedicated server**

No-render work should be a separate milestone after the four-client campaign test passes.

## KaiTOR manual launcher

`runtime/Start-KaiTORCampaignServer.ps1` reproduces the pinned launch contract without requiring a player client to remain the host.

Example:

```powershell
.\Start-KaiTORCampaignServer.ps1 `
  -BannerlordRoot 'D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord' `
  -SaveName 'KaiTOR_Test_01' `
  -Visibility none
```

The script:

- requires `Bannerlord.exe` in `bin\Win64_Shipping_Client`;
- targets exact build `1.3.15.110062` unless `-SkipVersionCheck` is explicitly supplied for diagnostics;
- reads `modules.vanilla-1.3.15.txt` by default;
- validates every requested module is installed;
- requires the `Coop` module;
- builds the exact `_MODULES_*...*_MODULES_` token expected by Bannerlord;
- uses `/server /coopsave` so the process auto-loads the named save;
- defaults visibility to `none` for a safe local/LAN test;
- never prints the server password;
- uses UDP `4200`, because the pinned 0.0.1 command-line contract does not expose a port argument.

Use `-DryRun` to validate the command without starting Bannerlord.

### Module list for the first vanilla runtime test

The shipped baseline is:

```text
Native
SandBoxCore
Sandbox
CustomBattle
StoryMode
Coop
```

This matches the generated Coop 1.3.15 dependency set plus `Coop` itself. TOR modules are deliberately excluded from this first runtime matrix.

For later TOR testing, pass a custom ordered file:

```powershell
.\Start-KaiTORCampaignServer.ps1 `
  -BannerlordRoot 'D:\...\Mount & Blade II Bannerlord' `
  -SaveName 'KaiTOR_TOR_01' `
  -ModuleListPath '.\modules.tor.txt'
```

The file must contain one module id per line in Bannerlord load order.

## First real runtime gate

The next runtime test is successful only if all of the following happen in the real `1.3.15.110062` process:

1. Standalone server reaches Coop startup and loads the named save.
2. A connects and recovers/creates player A.
3. B connects independently.
4. C connects independently.
5. D connects independently.
6. All four parties exist and move independently on one campaign map.
7. A fifth distinct controller is rejected before character creation.
8. Disconnect/reconnect of one existing controller does not create a duplicate party or consume a fifth slot.
9. Save server world.
10. Stop server completely.
11. Restart the same save.
12. Reconnect D -> B -> A -> C and verify original Hero/Clan/MobileParty ids are recovered.

Capture `Coop_server.log` and the server outputs of:

```text
coop.debug.players.list
coop.debug.players.party_state <controller_id>
```

before and after reconnect and again after server restart.

## What CI can and cannot prove

CI can prove:

- exact 1.3.15 reference/API compilation;
- server/client networking code builds;
- four-player admission semantics;
- peer-generation/reconnect invariants;
- launch-script command construction;
- ServerConsole auxiliary service startup;
- distributable packaging safety.

CI cannot legally or practically boot the user's installed Bannerlord campaign executable, so only the real runtime test can prove independent campaign movement, world ticking, save restoration and eventual TOR compatibility.

KaiTOR Co-op v1.3.15.13
TEST CANDIDATE — Bannerlord 1.3.15.110062 + The Old Realms 1.3.15
Languages: English / Russian
Maximum simultaneous players: 4
Co-op UDP port: 4200

INSTALL
1. Close Bannerlord and the TaleWorlds launcher.
2. Copy Modules\Coop from this package into <Bannerlord>\Modules\Coop.
   Replace the previous Coop folder when prompted.
3. The Old Realms 1.3.15 must be installed. KaiTOR does not redistribute TOR or Bannerlord files.
4. For the first live test, optional gameplay modules may be left disabled.
5. Optional Steam modules supported by this build: KaiCleave v1.3.15.31, KaiTOR Stability v1.3.15.50, and KaiTOR Portrait Fix v1.3.15.60. Server and every client must use the same active module set and exact versions. Workshop copies are staged temporarily by the launcher.

START THE AUTHORITATIVE CAMPAIGN SERVER
1. Run START_KAITOR_COOP.bat from the package root.
2. Select the Bannerlord root folder.
3. Enter the exact campaign save name.
4. Enter the server name. Default: KaiTOR Co-op | TOR RU Test.
5. Choose visibility and an optional password. Use Public to appear in the global Steam list (the server panel now defaults to Public).
6. Click Preflight. Do not start the server unless Preflight reports PASS.
7. Click Start Server.
8. The panel should show ONLINE after Bannerlord.exe /singleplayer /server starts.

JOIN FROM A CLIENT
1. Start Bannerlord 1.3.15.110062 with TOR 1.3.15 and KaiTOR Co-op enabled.
2. Required order: TOR_Armory -> TOR_Environment -> TOR_Core -> Coop.
3. In the main menu choose "Join KaiTOR Co-op".
4. Steam Lobbies is the default tab. Refresh the list, find the server by name, and join it.
5. Each row shows the server name, players x/4, KaiTOR version, and compatibility status. Incompatible builds cannot be joined.
6. Direct Connection remains available as a fallback; its default port is 4200.

STEAM / VPN
- Before a Public/Friends launch, Preflight verifies that Steam is running and local UDP 27315/27316 are free. These ports do NOT need router forwarding for the Steam P2P/Relay path; they only need to be available on the host PC.
- "Server: ONLINE" means the Bannerlord process is alive. The separate "Steam: LOBBY READY" state means the Steam game-server logged on, the lobby was created, and the P2P tunnel is listening.
- Compatibility is bound to the exact CI build: BuildVersion contains the commit SHA. Matching module numbers alone are not enough if the DLLs came from a different commit.
- Normal Steam Lobby joining uses the Steam P2P/relay tunnel, so players do not need to type the host's public IP.
- This is the preferred path for a host using a VPN or sitting behind NAT/CGNAT, provided Steam and Steam Networking work through that VPN.
- UDP 4200-4201 forwarding is only needed for direct-IP/fallback connectivity. The Steam tunnel does not require manual IP entry.

FIRST LIVE ACCEPTANCE
A. Connect player 1 and verify the campaign loads.
B. Connect player 2 and verify both players have independent heroes/parties and can move independently.
C. Connect players 3 and 4.
D. Verify a fifth distinct player is rejected.
E. On the authoritative server run: coop.debug.kaitor.snapshot4p
F. Verify four unique controller/hero/clan/mobile-party identities.
G. Complete one encounter and one battle without restarting the server.
H. Save, stop the server, start it again, then reconnect in a different order (D -> B -> A -> C).
I. Verify every player recovers the original hero/clan/mobile-party identity.

FAIL THE TEST IMMEDIATELY IF YOU SEE
TypeLoadException, MissingMethodException, MissingFieldException, FileLoadException,
BadImageFormatException, ReflectionTypeLoadException, Harmony patch failure,
duplicate hero/party identity, shared movement between players, or world-state desync.

LOGS
Use Open Log in the server panel. Keep the complete server log and relevant client logs when reporting a failure.

IMPORTANT
ServerConsole is auxiliary rendezvous/listing/NAT infrastructure. It is NOT the authoritative campaign world.
The authoritative world is the Bannerlord campaign-server process started with /singleplayer /server.
Native no-render/headless TOR operation is not claimed by this build.

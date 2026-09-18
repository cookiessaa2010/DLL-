KaiTOR Co-op v1.3.15.10
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

START THE AUTHORITATIVE CAMPAIGN SERVER
1. Run START_KAITOR_COOP.bat from the package root.
2. Select the Bannerlord root folder.
3. Enter the exact campaign save name.
4. Choose visibility and an optional password.
5. Click Preflight. Do not start the server unless Preflight reports PASS.
6. Click Start Server.
7. The panel should show ONLINE after Bannerlord.exe /singleplayer /server starts.

JOIN FROM A CLIENT
1. Start Bannerlord 1.3.15.110062 with TOR 1.3.15 and KaiTOR Co-op enabled.
2. Required order: TOR_Armory -> TOR_Environment -> TOR_Core -> Coop.
3. In the main menu choose "Join KaiTOR Co-op".
4. Use Direct Connection (server IP/host, optional :port) or Steam Lobbies.
5. Default port is 4200.

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

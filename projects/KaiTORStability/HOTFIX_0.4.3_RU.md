# KaiTOR Stability 0.4.3 — live monitor hotfix

0.4.2 failed the loading-screen UX acceptance test: the monitor could appear static until the main menu.

0.4.3 changes:
- no AboveNormal priority boost for Bannerlord during startup;
- no recursive shader-cache enumeration on the WinForms UI thread;
- current-session log offsets start at EOF instead of reparsing historical logs;
- game process is resolved by known executable names, executable path/window fallback, then exact PID from `SESSION_START`;
- 250 ms heartbeat stays visible during loading;
- CPU/RAM/current I/O plus cumulative read/write and active-sample count prove whether the game is doing work;
- shader cache size/file count is scanned only in a background task;
- first-launch shader-source prestage and bounded TOR file warmup are retained;
- runtime shader and KaiCleave telemetry remain active after the main menu.

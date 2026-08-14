WALLEPERF - Performance patches for 7 Days to Die V2.x
=======================================================

23 code-level Harmony patches targeting the game's measured frame-time
hotspots: ~+7% average FPS, ~+15% smoother 1% lows during hordes, the
block-place/dig hitch fixed, HUD cost halved, pathfinding spam removed,
distant-zombie shadow/animation load shedding.

Install
-------
1. Run install-WallePerf.bat (finds your game automatically; asks if not).
   Or manually: copy the WallePerf folder into "...\7 Days To Die\Mods\".
2. Launch the game with EasyAntiCheat DISABLED (launcher checkbox).
   Code mods never load under EAC - this is normal for all DLL mods.
3. Works in singleplayer and client-side in multiplayer; servers can run
   it too for server-side gains.

Verify: press F1 in game, look for "[WallePerf] ... patches active".

Configure
---------
Every patch has an on/off toggle in WallePerf\WallePerfConfig.xml.
Console: "walleperf on|off|status" toggles all patches live, no restart.
One experimental patch (TerrainTangentSkip) ships disabled by default.

Uninstall: delete the WallePerf folder from the game's Mods folder.

Source, full performance audit and issue tracker:
https://github.com/EYamanS/7dtd-walle-mods

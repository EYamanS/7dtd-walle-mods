WALLEQOL - Quality of life for 7 Days to Die V2.x
==================================================

Two features for co-op play:

* SHARED CONTAINERS AND STATIONS - several players can use the same
  chest, workbench, forge, cabinet, car or dropped bag at the same
  time, with live-syncing windows. No more "container in use".
  Built on the game's own shared-lock system (the one traders use).
  Per-scope toggles in WalleQoLConfig.xml (player storage, world
  loot, workstations, dropped bags); quest containers stay vanilla.
  Note: two players editing a workstation CRAFT QUEUE in the same
  instant is last-write-wins; item slots sync live.

* QUICK DEPOSIT - hold E on any of your placed containers and pick
  "Deposit Items": tops up all matching stacks in the chest straight
  from your backpack without opening it. Shows -N item entries in the
  pickup feed, respects locked backpack slots, skips chests you are
  locked out of.

* CRAFT FROM CONTAINERS - crafting and item repair pull missing
  ingredients straight from your placed storage within range
  (default 15m, configurable). Backpack and toolbelt are used first,
  chests only cover the shortfall; the pickup feed shows what came
  out of storage. Container locks and user-locked container slots
  are respected. Trader purchases, vending rent and lockpicks always
  use your real inventory (no exploits). World loot is never touched.

Install
-------
1. Run install-WalleQoL.bat (finds your game automatically; asks if not).
   Or manually: copy the WalleQoL folder into "...\7 Days To Die\Mods\".
2. Launch the game with EasyAntiCheat DISABLED (launcher checkbox).
   Code mods never load under EAC - this is normal for all DLL mods.
3. Multiplayer: the HOST and EVERY player need this mod (shared
   containers are decided server-side).

Verify: press F1 in game, look for "[WalleQoL] SharedContainers /
QuickDeposit: enabled".

Configure
---------
Each feature can be turned off in WalleQoL\WalleQoLConfig.xml.

Uninstall: delete the WalleQoL folder from the game's Mods folder.

Source and issue tracker:
https://github.com/EYamanS/7dtd-walle-mods

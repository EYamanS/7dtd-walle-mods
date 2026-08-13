WALLE MODS for 7 Days to Die v2.5
==================================

What's inside
-------------
WallePerf v0.3 - performance patches: ~+7% average FPS and ~+15% smoother
  1%-lows during hordes, fixes the block-place/dig hitch, halves HUD cost.
  Every patch can be toggled in WallePerf\WallePerfConfig.xml.

WalleQoL v0.1 - quality of life:
  * Shared containers: several players can open the same chest at once.
  * "Deposit Items" on the hold-E radial menu of your placed containers:
    tops up matching stacks from your backpack without opening the chest.
    Respects your locked backpack slots.

Install
-------
1. Run install.bat (it finds your game automatically; if it can't, it asks
   for the game folder). Or manually: copy the WallePerf and WalleQoL
   folders into "...\7 Days To Die\Mods\".
2. Launch the game with EasyAntiCheat DISABLED (launcher checkbox).
   DLL mods never load under EAC - this is normal for all code mods.
3. Multiplayer: host AND all players need the mods installed.

Verify it works
---------------
Press F1 in game and look for lines like:
  [WallePerf] 22/23 patches active
  [WalleQoL] SharedContainers: enabled
  [WalleQoL] QuickDeposit: enabled

Uninstall
---------
Delete the WallePerf and WalleQoL folders from the game's Mods folder.

Notes
-----
* Reinstalling/updating overwrites the config files (toggles reset to
  defaults - all on, which is the recommended setup).
* If anything ever acts strange, you can turn individual patches off in
  WallePerfConfig.xml, or type "walleperf off" in the F1 console to
  disable all performance patches live.

Built by Yaman + Claude from a decompiled-source performance audit.

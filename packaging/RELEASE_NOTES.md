Performance + QoL mods for **7 Days to Die V2.x** (built and tested on v2.5), created from a code-level frame-time audit of the decompiled game.

## Download & install

1. Download **Walle-Mods-v*.zip** below and extract it anywhere
2. Run **install.bat** (finds your game automatically)
3. Launch the game with **EasyAntiCheat disabled** (launcher checkbox) — code mods never load under EAC
4. Multiplayer: the host **and** every player need the mods

Verify in-game: press F1 and look for `[WallePerf] ... patches active` and `[WalleQoL] ... enabled`.

## What you get

**WallePerf** — 23 Harmony performance patches: benchmarked at **~+7% average FPS** and **~+15% smoother 1% lows during hordes**, plus the block-place/dig hitch fixed, HUD cost halved, pathfinding spam removed, and distant-zombie shadow/animation load shedding. Every patch toggleable in `WallePerfConfig.xml`; `walleperf on|off` in the console toggles them live.

**WalleQoL** —
- **Shared containers & stations**: several players can use the same chest, workbench, forge, cabinet, car or dropped bag at the same time (live-syncing windows, uses the game's own shared-lock system). Per-scope toggles in the config; quest containers stay vanilla.
- **Quick deposit**: "Deposit Items" on the hold-E radial of your placed containers — tops up matching stacks from your backpack without opening the chest, with `-N item` feed entries. Respects locked backpack slots.
- **Craft from containers**: crafting and item repair pull missing ingredients from your placed storage within range (default 15m). Works at campfire, workbench, chemistry station, cement mixer AND the forge (smelted bank units used first, chest smeltables cover the shortfall at material value, change credited back to the bank). Backpack first, containers for the shortfall; locks and reserved (user-locked) container slots respected; trader/vending/lockpick costs always come from your real inventory.

The zip contains the two gameplay mods. The source repo also has **WalleBench** (`bench auto` — automated A/B benchmark + subsystem profiler) if you want to measure your own machine.

Full performance audit: see `ANALYSIS.md` in the repository.

# 7DTD Modding Workspace

Custom performance + QoL mods for 7 Days to Die v2.5, built from a decompiled-source analysis.

## Layout

| Path | What |
|---|---|
| `decompiled\` | Full decompiled game source (Assembly-CSharp, ilspycmd) — reference only |
| `ANALYSIS.md` | The performance audit: 24 ranked findings + 3 vanilla bugs, with file:line cites |
| `src\WallePerf\` | Performance mod — 11 Harmony patches (Tier 1 of the audit) + `walleperf on/off` runtime toggle |
| `src\WalleQoL\` | QoL mod — shared chest access + quick deposit radial command (with −N item toasts) |
| `src\WalleBench\` | Benchmark mod — `bench base` / `bench horde` console commands, CSV results |
| `src\Directory.Build.props` | Game paths + assembly references shared by both projects |

## Build & deploy

```powershell
dotnet build C:\Users\Yaman\7dtd-modding\src\WallePerf\WallePerf.csproj
dotnet build C:\Users\Yaman\7dtd-modding\src\WalleQoL\WalleQoL.csproj
```

Building auto-copies the DLL + ModInfo.xml into the game's `Mods\` folder. Config XMLs are
only copied if not already present (your toggles survive rebuilds).

## Requirements to play with these mods

- **Launch the game with EAC disabled** (game launcher → uncheck EasyAntiCheat). DLL mods
  never load under EAC (`SkipWithAntiCheat` makes them skip gracefully instead of erroring).
- For multiplayer: install both mods on the **host and every player**.
  - QuickDeposit technically works client-side only.
  - SharedContainers *requires* the host/server to have it (lock decisions are server-side).

## Toggles

- `Mods\WallePerf\WallePerfConfig.xml` — every performance patch on/off individually.
- `Mods\WalleQoL\WalleQoLConfig.xml` — SharedContainers / QuickDeposit on/off.

Changes take effect on next game start. Check the game log (`%AppData%\..\LocalLow\The Fun
Pimps\7 Days To Die\Player.log`) for `[WallePerf]` / `[WalleQoL]` lines to confirm what loaded.

## What the mods do

**WallePerf v0.1** (see ANALYSIS.md Tier 1 for the full story):
frame-time patches — music threat scan cached (was a 50m entity sweep per frame), weather
ground-probe spherecast bounded (was infinite), UI binding parse skipped when unchanged,
compass/nav icons at 12Hz, obstacle raycasts throttled for zombies >15m away, per-tick
entity-activity sort at 4Hz, dead mesh-validation loop removed, plus assorted per-frame
allocation and early-out fixes.

**WalleQoL v0.1**:
- *SharedContainers*: player-placed chests use the LockManager's shared-lock mode (same
  mechanism traders use), so several players can open one chest at once. Includes a sync fix
  so open windows live-update instead of discarding other players' changes. Known limit: two
  players grabbing the exact same stack within the same network round-trip can still race.
- *QuickDeposit*: "Deposit Items" on the hold-E radial of player-placed containers — tops up
  existing stacks from your backpack (same as the single-arrow button inside), without
  opening the container. Respects locked backpack slots and chest locks. Batched into a
  single network packet.

## Benchmarking — one command

In-game console (F1):

```
bench auto
```

That's it. It god-modes you, locks your position, disables VSync, fixes the time of day,
then runs four segments automatically — base and a deterministic 60-zombie ring, each with
patches ON and OFF (toggled at runtime) — restores all your settings, and prints an A/B
comparison table. Takes ~2.5 minutes. Optional: `bench auto <baseSec> <hordeCount> <hordeSec>`.

Manual pieces still exist: `bench base [sec]`, `bench horde [count] [sec]`, `bench clear`,
`walleperf on|off|status`. Every run appends to `Mods\WalleBench\bench_results.csv`.

## Next up (from ANALYSIS.md)

Tier 2: pathfinding request dedup, path-smoothing cap, block-change chunk rebuild budgeting,
terrain mesh async upload, autosave off main thread, HUD refresh throttles, EffectManager
tag-parse hoist. Then Tier 3's bigger surgery if profiling justifies it.

# 7 Days to Die v2.5 — Performance Analysis (Code-Level)

Decompiled source: `C:\Users\Yaman\7dtd-modding\decompiled` (Assembly-CSharp.dll, ilspycmd, game v2.5, Unity 2022.3.62, Mono).
Analysis date: 2026-08-13. Four subsystem audits: game loop, AI/pathfinding, chunk/mesh/streaming, rendering-adjacent managers.
All patches target the game's native Harmony loader (`Mods\0_TFP_Harmony`); EAC must be off.

## The big picture

The game is CPU **main-thread bound**. Frame-time cost splits into three symptom groups:

- **Steady-state per-frame waste** (drags average fps at all times): music threat scan, HUD/UI binding re-parse, nav icon re-evaluation, weather spherecast, per-frame allocations.
- **Horde-night throughput** (fps collapse with many zombies): un-throttled movement physics for distant entities, path request spam, main-thread path smoothing, per-tick entity sorting.
- **Hitches/stutter** (frame spikes): synchronous terrain mesh upload, forced same-frame chunk rebuilds on block change, main-thread autosave serialization, light flood-fill, dynamic-mesh (distant POI) builds.

---

## TIER 1 — High impact, low risk (first release: "WallePerf v0.1")

### T1.1 Music threat-level scan every frame ⭐ biggest single win
`EntityPlayerLocal.Update` (EntityPlayerLocal.cs:3227) → `ThreatLevelUtility.GetThreatLevelOn` (DynamicMusic\ThreatLevelUtility.cs:35-68).
Every frame: `GetEntitiesInBounds(typeof(EntityEnemy), 50³ bounds)` walks ~49 chunks through a `ReaderWriterLockSlim`, reflection `IsAssignableFrom` per entity (Chunk.cs:3004); blood moon adds UniLinq `.Average()` over a 300-element queue; plus `GamePrefs.GetString` + biome string compares per frame.
**Patch:** Prefix on `GetThreatLevelOn`: return cached result, recompute every 10–15 frames. Static, self-contained, fully safe.

### T1.2 Weather emitter spherecast: radius 9 m, distance ∞, every frame
`WeatherManager.ParticlesFrameUpdate` (WeatherManager.cs:1719-1898, cast at 1732): `Physics.SphereCast(ray_down_from_camera+250m, 9f, out hit, float.PositiveInfinity, mask)` every frame even with zero precipitation.
**Patch:** Prefix: skip when no rain/snow/storm active; else run every N frames (method already supports cached `particleFallLastPos`); or Transpiler ∞→500f.

### T1.3 XUi `BindingInfo.RefreshValue` re-parses attributes even when unchanged
BindingInfo.cs:101-190. Change-detection flag only gates rebuilding `cachedResultValue` (123-142); the parse/assign block (143-186) runs unconditionally for every binding of every refreshing controller, every frame (compass, stat bars, buff lists…).
**Patch:** Prefix replicating method but skipping parse block when `!flag && parsersDetected` (fields public via PublicizedFrom). Benefits every UI window globally.

### T1.4 NavObjectManager re-evaluates every compass/map icon every frame
NavObjectManager.cs:341-360 → NavObject.cs:204-344, 473-492. Per icon per frame: string-keyed CVar lookups + full `EffectManager.GetValue` stack walks (Tracking/TreasureRadius).
**Patch:** Prefix on `NavObjectManager.Update` running original at 4–10 Hz (`return false` otherwise). UI-only latency, invisible. Lowest-risk high-value patch.

### T1.5 Distant zombies pay full movement-physics cost
`EntityAlive.updateTasks` (EntityAlive.cs:5646-5697): `aiActiveScale` throttles only `EAIManager.Update`; `navigator.UpdateNavigation()` + `moveHelper.UpdateMoveHelper()` run every tick for every entity. `EntityMoveHelper.cs:636-696`: every 4 ticks `CheckEntityBlocked` (SphereCast, :1115) + `CheckWorldBlocked` (2-3 `Voxel.Raycast` sphere casts, :843-891).
**Patch:** Prefix on `EntityMoveHelper.UpdateMoveHelper`: stretch `obstacleCheckTickDelay` (or early-out) when `entity.aiClosestPlayerDistSq > 225` (public field, refreshed by EntityActivityUpdate). Big horde-night win.

### T1.6 `World.EntityActivityUpdate` sorts all entities per player, 20×/s (flagged by 2 agents)
World.cs:2627-2694, called from TickEntities (:2510) every tick: O(E×P) `GetClosestPlayer` scans + `aiClosest.Sort(Comparison)` per player (allocates comparer wrapper) + JiggleOn/ClothSimOn per entity.
**Patch:** Prefix running body every 5 ticks. Consumers (`aiActiveScale`, `aiClosestPlayerDistSq`) tolerate 250 ms staleness.

### T1.7 `MeshDataManager.PreValidateJobData` — pure validation loop, O(indexCount), main thread
MeshDataManager.cs:515-527, called from `Add` (:539) for every blocks/models/grass mesh upload — up to 131k iterations per mesh, only logs on failure.
**Patch:** Prefix `__result = true; return false;`. Trivial and safe.

### T1.8 Small free wins (bundle)
- `GameManager.ExplodeGroupFrameUpdate` (GameManager.cs:3890): `EntityClass.FromString("fallingBlock")` every frame even when `explodeFallingGroups` empty → Prefix early-out.
- `ObjectiveRallyPoint.SetupFlags` (ObjectiveRallyPoint.cs:146-180): `new HashSet` every frame + `GetComponent` per visible rally objective → static cached set + throttle.
- Achievement stats pushed every frame (EntityPlayerLocal.cs:3209-3219) → Prefix on `SetAchievementStat` impls: early-out when unchanged.
- `Entity.Update` (Entity.cs:844-886, alloc at :873): `new List<StopAnimatorAudioType>()` per frame per entity with animator audio → static scratch list.
- `CharacterShaderLODControl.Update` (CharacterShaderLODControl.cs:32-44): 2× `Camera.main` + unconditional `material.shader.maximumLOD` writes per material per frame → cache last LOD, return false when unchanged.
- `XUi.OnUpdateDeltaTime` (XUi.cs:850-857): `windowManager.Open(ToolTip/SaveIndicator)` every frame → gate on `!IsWindowOpen`.

---

## TIER 2 — High impact, medium effort

### T2.1 Path request spam while chasing
`EAIApproachAndAttackTarget.Update` (EAIApproachAndAttackTarget.cs:346-373): re-path every 0.3–0.8 s per chasing zombie; :338-345 zeroes `pathCounter` when ≤2 nodes left (immediate re-request). Each request allocates PathInfoSingleTarget/ASPPathFinder/ABPath/TraversalProvider (ASPPathFinder.cs:103).
**Patch:** Prefix state-cache: skip re-path when `entityTarget` moved <~1.5 m since last request (`entityTargetPos` tracked at :273). Classic "zombies eat the server" fix.

### T2.2 Main-thread path smoothing: 50–100+ physics casts per finished path
`ASPPathFinder.OnPathFinished` (GamePath\ASPPathFinder.cs:134-256; corner loop 192-207; `IsLineClear` 259-292 = up to 2 Linecast + 2 SphereCast per pair). Runs on main thread per completed path, multiplied by T2.1.
**Patch:** Prefix on `IsLineClear` returning false beyond distance from nearest player (degrades smoothing only), or cap smoothing iterations via Transpiler.

### T2.3 Block change near player → up to 9 chunks × 16 layers rebuilt same frame
`ChunkCluster.chunkPosNeedsRegeneration` (ChunkCluster.cs:1133-1158) fills `ChunksToCopyInOneFrame`; `ChunkManager.CopyChunksToUnity` (ChunkManager.cs:601-610) drains it in a do/while ignoring the 2.5 ms budget, via `CreateMeshAll` (ChunkGameObject.cs:266-281, no slicing).
**Patch:** Postfix clearing `ChunksToCopyInOneFrame` (fall back to budgeted path) or Prefix replacing the do/while. Trades 1 frame of visual latency for no spike. The dig/build hitch fix.

### T2.4 Terrain meshes bypass async upload; RecalculateTangents on main thread
`VoxelMeshTerrain.CopyToMesh` (VoxelMeshTerrain.cs:408-478): synchronous vertex/UV/color copies, per-submesh SetTriangles (:455-459), `RecalculateTangents` (:472), `RecalculateUVDistributionMetrics` (:473), `UploadMeshData` (:475) — largest meshes in game (65k default, up to 786k verts; VoxelMesh.cs:736). Base-class path uses `MeshDataManager` async route (VoxelMesh.cs:494-544); terrain doesn't.
**Patch:** stage 1: skip RecalculateTangents/UVMetrics (verify terrain shader doesn't need tangents). Stage 2: route through MeshDataManager (needs submesh support). Primary exploration-hitch fix.
Related: `VoxelMeshTerrain.ApplyMaterials` (:333-373) allocates `new Material[]`/`new Material` per submesh per upload → cache by texture-id key.

### T2.5 Autosave serializes chunks on main thread every 2 s
GameManager.cs:1662-1665 `SaveRandomChunks(2,…)` every 40 ticks → RegionFileChunkSnapshot.Update (RegionFileChunkSnapshot.cs:12-43) full `chunk.save(writer)` on main thread. `Chunk.SetLight` sets `isModified` on any light change (Chunk.cs:1647) keeping chunks dirty. `MakePersistent` (RegionFileManager.cs:1449-1504) serializes ALL dirty chunks on caller thread; `WaitSaveDone` (:1506-1526) sleeps in a loop = save/exit freeze.
**Patch:** Prefix dispatching serialization to `ThreadManager.AddSingleTask` (off-thread serialization already an established pattern in `DoSaveChunks`; chunk flagged `InProgressSaving`).

### T2.6 HUD steady-state cost
- `XUiC_CompassWindow.Update` (XUiC_CompassWindow.cs:51-101, bindings 108-137): `RefreshBindings()` every frame; 3× `EffectManager.GetValue(NoTimeDisplay)` per frame; `updateMarkers` over ~13 lists with `RefreshData()` + `GetValue(TreasureRadius)` per chest per frame (:302, :459). → throttle to ~10 Hz.
- `XUiC_HUDStatBar.Update` (XUiC_HUDStatBar.cs:126-212): refreshes bindings every frame while HUD hidden (:133-139); fill lerp `t=dt*3` (:262-283) keeps `hasChanged()` true for dozens of frames → refresh once on hide transition; snap lerp when |current−target| < 0.001.
- `XUiC_OnScreenIcons.OnScreenIcon.Update` (XUiC_OnScreenIcons.cs:32-227, string.Format at :212): 2 string allocs + NGUI geometry rebuild per icon per frame → update text only when displayed value changes.

### T2.7 EffectManager hot-path waste (cross-cutting)
EffectManager.cs:77-192: every call starts with `MinEventParams.CopyTo` (22 field copies); :187 (and GetValuesAndSources :329) call `FastTags.Parse(_passiveEffect.ToStringCached())` INSIDE the loop over item Modifications — re-parsing per installed item-mod per call; `GetValuesAndSources` allocates a List per call (:281, hit every 0.5 s per entity via EntityStats.cs:142).
**Patch:** Transpiler hoisting the Parse out of the loop (easy, safe). Broader per-tick memoization = higher effort/risk, later.

### T2.8 Crosshair spread: uncached GetComponent + 2 effect-stack walks per frame
EntityPlayerLocal.cs:3262-3264: `cameraTransform.GetComponent<Camera>()` (already cached in `playerCamera`, assigned :1233) + 2× `EffectManager.GetValue(SpreadDegrees*)` per frame with ranged weapon.
**Patch:** Transpiler swapping GetComponent→ldfld playerCamera; cache spread at ~4 Hz.

### T2.9 UAI (bandits/drones) sight raycasts bypass the see-cache + per-decision allocations
UAI\UAIBase.cs:103-118: sorter objects allocated per 0.2 s decision; UAIConsiderationTargetVisible.GetScore calls `CanEntityBeSeen` directly (bypasses EntitySeeCache) once per action per target per decision.
**Patch:** memoize visibility per (self,target) within a decision pass; hoist sorters to static readonly.

### T2.10 Misc AI
- `EAIDodge.CanExecute` (EAIDodge.cs:44, 75-86): 10 Hz chunk-entity scan waiting for an attacker that rarely exists → Prefix: require attackTarget/nearby player, or bump executeDelay.
- `EAIApproachAndAttackTarget.cs:299-318`: per-tick weapon range recompute incl. `EffectManager.GetItemValue(MaxRange)` at 20 Hz → cache until `holdingItemItemValue.type` changes.
- `EAISetNearestEntityAsTarget.FindTarget` (EAISetNearestEntityAsTarget.cs:130-211): chunk scans + sort per non-player target class every ~0.5 s; `.magnitude`/`GetDistance` sqrt where squared works (:154, :198, :216) → skip non-player scans when `aiClosestPlayerDistSq` large; cache scan results seconds.
- `EntitySeeCache` wiped every 30 ticks (EntitySeeCache.cs:78-85) → extend to 60 ticks for global halving of sight raycasts (small reaction-latency cost).

---

## TIER 3 — Bigger surgery / riskier (after profiling confirms)

### T3.1 Light flood-fill on block change (mining/explosion hitches)
`ChunkCluster.SetBlock` (ChunkCluster.cs:799-823) → LightProcessor.cs: full 256-column sunlight refresh (:54-101), recursive Spread/UnspreadLight (:140-245), `GetChunkFromWorldPos` locked dict lookup per cross-chunk voxel (:106, :171, :226); every lit voxel dirties a mesh layer (Chunk.cs:1627-1648).
**Patch:** replace with iterative queue-based version caching current-chunk ref.

### T3.2 OcclusionManager fixed costs + camera-turn oscillation
OcclusionManager.cs: `RenderOccludees` (:1143-1168) draws all 8×511=4088 instanced boxes (~260 KB matrix upload) per frame regardless of usage; extra `depthCamera.Render()` per frame (:1196-1222); `LocalPlayerOnPreCull` (:1170-1193) re-enables ALL renderers whenever view direction deviates >~20° (Dot<0.94) — oscillates during mouse-look (`SetRenderersEnabled` :1251-1278).
**Patch:** submit only used matrix units (track per-unit counts via Register/Unregister postfixes); relax 0.94 threshold. Alternatively test `occlusion off` in console for A/B.

### T3.3 Sleeper volumes: all volumes ticked every tick under lock
`World.TickSleeperVolumes` (World.cs:4703-4713, called :1893); SleeperVolume.Tick (SleeperVolume.cs:380-433) iterates respawnMap + GetEntity per spawned sleeper while spawned.
**Patch:** rotating 1/N slice. ⚠ `--ticksUntilDespawn == 0` (:429) is exact-equality — decrement by N or clamp, or despawn never fires.

### T3.4 TickEntity chunk lookups through RW-lock 2×/entity/tick
World.cs:2551-2615 (:2558, :2579) + WorldChunkCache.cs:68-74.
**Patch:** Prefix-replace TickEntity caching Chunk ref while `chunkPosAddedEntityTo` unchanged.

### T3.5 DynamicMesh (distant POI) pipeline
- `DynamicMeshVoxelLoad.CopyTerrain` (DynamicMeshVoxelLoad.cs:22-151): main-thread mesh build, RecalculateNormals (:124), budget checked after whole mesh.
- `DyMeshRegionLoadRequest.CreateOpaqueMesh/CreateTerrainGo` (DyMeshRegionLoadRequest.cs:52-222): 160 m region imposters (100k+ verts) uploaded with unbounded Set* calls.
- `DynamicMeshManager.cs`: per-frame `new List` (:1791); `GetNearestUnloadedRegion` (:1623-1648) 2 LINQ chains, one result DISCARDED; `CheckGameObjects` (:2306-2335) string-parses GameObject names; `DisabledImposterChunkManager.Update` (:56-79) iterates entire world chunk grid (262k for 8k map) + full ComputeBuffer re-upload when dirty.
- `ChunkChanged` (:2048-2089): imposter regen per block change + neighbors. **Vanilla bug** :2080-2087 — z==15 neighbor case gated on `num == 15` (x) instead of z.
**Patch:** route terrain imposters through MeshDataManager; replace LINQ; throttle AddUpdateData.

### T3.6 Mesh generation single-threaded with fixed sleeps
`ChunkManager.thread_Regenerating` (ChunkManager.cs:965-1016): ~1 visual + 1 collider chunk per iteration then sleep 5 ms; hard-stalls 20 ms when VML pool backpressured (main thread upload too slow → generation stops entirely). Fast-travel chunk-appearance bottleneck.
**Patch:** return 0 when work remains (easy); additional regen threads possible (per-chunk write locks exist) but medium concurrency risk.

---

## Vanilla bugs found (worth reporting/fixing regardless)
1. `NetEntityDistribution.OnUpdateEntities` (NetEntityDistribution.cs:113): `float num2 = vector.x * vector.x + vector.z + vector.z;` — z added, not squared → network priority distance wrong on servers.
2. `DynamicMeshManager.ChunkChanged` (DynamicMeshManager.cs:2080-2087): z-boundary neighbor update gated on x==15 instead of z==15.
3. `Audio.Manager` occlusion branch (off by default): `volume / maxVolume * maxVolume` no-op + per-frame GetComponent/string allocs/infinite raycasts if ever enabled.

## Confirmed clean (don't touch)
Entity tick slicing (TickEntitiesSlice), WorldBlockTicker budgets, CopyChunksToUnity µs budget (except the ChunksToCopyInOneFrame bypass), PowerManager 0.16 s cadence, GameLightManager/LightLOD time-slicing, PrefabLODManager 1 Hz, EnvironmentAudioManager, XUiUpdater non-showing-group skip, toolbelt/quest-tracker dirty-flag gating, AstarManager graph update merging, MemoryPools for VoxelMeshLayer.

## Patch-safety notes
- All cited members are public or `[PublicizedFrom]` (publicized assembly) — standard HarmonyX Prefix/Postfix works; verify runtime visibility.
- Avoid patching tiny non-virtual methods callers might inline (EntitySeeCache members — patch call sites or the class's own methods carefully).
- Ship every patch behind a config toggle; log applied patches at startup.
- Test protocol: `dm` + `debugtime`/Unity profiler equivalent; A/B via toggles; horde night worst case; dedicated server sanity check.

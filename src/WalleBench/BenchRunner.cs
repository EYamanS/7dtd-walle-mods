using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WalleBench
{
	// Frame-time recorder + fully automated A/B sequence ("bench auto").
	public class BenchRunner : MonoBehaviour
	{
		public class Result
		{
			public string label;
			public bool perfOn;
			public int frames;
			public float seconds, avgFps, onePctLowFps, minFps, avgMs, p99Ms, maxMs, heapDeltaMb;
			public int gcCollections, zombies;
			public List<SubsystemRow> subsystems = new List<SubsystemRow>();
		}

		public struct SubsystemRow
		{
			public string name;
			public float msPerFrame;
			public float callsPerFrame;
		}

		public static BenchRunner Instance;
		public static string CsvPath;

		[NonSerialized] public readonly List<int> spawnedEntityIds = new List<int>();

		// measurement state
		List<float> frameTimes;
		float duration;
		float elapsed;
		int warmupFrames;
		int gcStart;
		long heapStart;
		string currentLabel;
		bool measuring;
		Result lastResult;

		// auto-sequence state
		bool sequenceRunning;
		Vector3 anchorPos;
		bool anchorActive;

		public static void Ensure(string modPath)
		{
			if (Instance != null)
			{
				return;
			}
			GameObject go = new GameObject("WalleBench");
			DontDestroyOnLoad(go);
			Instance = go.AddComponent<BenchRunner>();
			CsvPath = Path.Combine(modPath, "bench_results.csv");
		}

		public bool IsBusy => measuring || sequenceRunning;

		// ---------------- manual single measurement (bench base / bench horde) ----------------

		public void BeginManual(string _label, float _seconds)
		{
			StartCoroutine(ManualRoutine(_label, _seconds));
		}

		IEnumerator ManualRoutine(string _label, float _seconds)
		{
			yield return Measure(_label, _seconds);
			DespawnAll();
			Report(lastResult);
			WriteCsv(lastResult);
		}

		// ---------------- subsystem profiler (bench profile) ----------------

		public void BeginProfile(float seconds)
		{
			StartCoroutine(ProfileRoutine(seconds));
		}

		IEnumerator ProfileRoutine(float seconds)
		{
			sequenceRunning = true;
			BenchProfiler.EnsureApplied();
			BenchProfiler.Reset();
			int frames = 0;
			float time = 0f;
			int gcBefore = GC.CollectionCount(0);
			Out($"[Bench] profiling subsystems for {seconds:0}s — play normally (fight, walk, whatever you want measured)...");
			BenchProfiler.Recording = true;
			while (time < seconds)
			{
				yield return null;
				frames++;
				time += Time.unscaledDeltaTime;
			}
			BenchProfiler.Recording = false;
			sequenceRunning = false;

			float totalMsPerFrame = time / frames * 1000f;
			var rows = new List<(string name, float msPerFrame, float callsPerFrame)>();
			foreach (BenchProfiler.Slot slot in BenchProfiler.Slots)
			{
				if (slot.calls > 0)
				{
					rows.Add((slot.name, BenchProfiler.TicksToMs(slot.ticks) / frames, (float)slot.calls / frames));
				}
			}
			rows.Sort((a, b) => b.msPerFrame.CompareTo(a.msPerFrame));

			Out($"[Bench] ========= PROFILE: {frames} frames over {time:0.0}s, avg {totalMsPerFrame:0.00} ms/frame ({frames / time:0.0} FPS) =========");
			Out("[Bench]  ms/frame  share   calls/f  subsystem");
			float accounted = 0f;
			foreach (var row in rows)
			{
				Out($"[Bench]  {row.msPerFrame,7:0.000}  {row.msPerFrame / totalMsPerFrame * 100f,5:0.0}%  {row.callsPerFrame,7:0.0}  {row.name}");
				if (!row.name.StartsWith("Main loop total"))
				{
					accounted += row.msPerFrame;
				}
			}
			Out($"[Bench]  ---------");
			Out($"[Bench]  NOTE: subsystems are inclusive and overlap (AI/movement run inside entity ticking, which runs inside the main loop).");
			Out($"[Bench]  Everything not listed = engine-side: rendering, animation/skinning, physics solve, GPU wait.");
			Out($"[Bench]  GC collections during profile: {GC.CollectionCount(0) - gcBefore}");
		}

		// ---------------- the one-command sequence (bench auto) ----------------

		public void BeginAuto(float baseSec, int hordeCount, float hordeSec)
		{
			StartCoroutine(AutoRoutine(baseSec, hordeCount, hordeSec));
		}

		IEnumerator AutoRoutine(float baseSec, int hordeCount, float hordeSec)
		{
			sequenceRunning = true;
			World world = GameManager.Instance.World;
			EntityPlayerLocal player = world.GetPrimaryPlayer();

			// --- save state we are about to mess with ---
			bool patchesWereOn = Harmony.HasAnyPatches("walle.perf");
			bool godWas = player.IsGodMode.Value;
			bool noCollWas = player.IsNoCollisionMode.Value;
			int vsyncWas = QualitySettings.vSyncCount;
			int targetFpsWas = Application.targetFrameRate;

			Out("[Bench] AUTO sequence starting — hands off, this takes ~" + Mathf.RoundToInt(2f * (baseSec + hordeSec) + 20f) + "s.");
			Out("[Bench] god mode ON, position locked, VSync OFF for the duration.");

			// --- controlled environment ---
			player.IsGodMode.Value = true;
			player.IsNoCollisionMode.Value = true;
			QualitySettings.vSyncCount = 0;
			Application.targetFrameRate = -1;
			Exec("settime 8 0 0");
			anchorPos = player.position;
			anchorActive = true;
			yield return new WaitForSeconds(2f); // let the time jump settle

			var results = new List<Result>();

			// interleave so world drift hits both sides equally: base ON, base OFF, horde ON, horde OFF
			foreach (bool on in new[] { true, false })
			{
				yield return SetPatches(on);
				yield return Measure("base", baseSec);
				results.Add(lastResult);
				Report(lastResult);
				WriteCsv(lastResult);
			}
			foreach (bool on in new[] { true, false })
			{
				yield return SetPatches(on);
				int spawned = SpawnRing(world, player, hordeCount);
				Out($"[Bench] spawned {spawned} zombies in a deterministic ring");
				yield return new WaitForSeconds(3f); // let them aggro and start moving
				yield return Measure("horde" + spawned, hordeSec);
				results.Add(lastResult);
				Report(lastResult);
				WriteCsv(lastResult);
				DespawnAll();
				yield return new WaitForSeconds(2f);
			}

			// --- restore everything ---
			yield return SetPatches(patchesWereOn);
			player.IsGodMode.Value = godWas;
			player.IsNoCollisionMode.Value = noCollWas;
			QualitySettings.vSyncCount = vsyncWas;
			Application.targetFrameRate = targetFpsWas;
			anchorActive = false;
			sequenceRunning = false;

			// --- comparison table ---
			Out("[Bench] ============== A/B COMPARISON ==============");
			Compare(results, "base");
			Compare(results, "horde");
			Out("[Bench] ============================================");
			Out("[Bench] full data: " + CsvPath);
		}

		void Compare(List<Result> results, string labelPrefix)
		{
			Result on = results.Find(r => r.label.StartsWith(labelPrefix) && r.perfOn);
			Result off = results.Find(r => r.label.StartsWith(labelPrefix) && !r.perfOn);
			if (on == null || off == null)
			{
				return;
			}
			Out($"[Bench] {labelPrefix,-6}  avg: {off.avgFps,6:0.0} -> {on.avgFps,6:0.0} FPS  ({Pct(off.avgFps, on.avgFps)})");
			Out($"[Bench] {"",-6}  1%low: {off.onePctLowFps,5:0.0} -> {on.onePctLowFps,6:0.0} FPS  ({Pct(off.onePctLowFps, on.onePctLowFps)})");
			Out($"[Bench] {"",-6}  worst frame: {off.maxMs:0.0} -> {on.maxMs:0.0} ms");
			// which subsystems the patches actually changed (OFF -> ON ms/frame)
			var deltas = new List<(string name, float offMs, float onMs)>();
			foreach (SubsystemRow offRow in off.subsystems)
			{
				float onMs = 0f;
				foreach (SubsystemRow onRow in on.subsystems)
				{
					if (onRow.name == offRow.name)
					{
						onMs = onRow.msPerFrame;
						break;
					}
				}
				deltas.Add((offRow.name, offRow.msPerFrame, onMs));
			}
			deltas.Sort((a, b) => Mathf.Abs(b.offMs - b.onMs).CompareTo(Mathf.Abs(a.offMs - a.onMs)));
			int shown = 0;
			foreach ((string name, float offMs, float onMs) in deltas)
			{
				if (shown >= 5 || Mathf.Abs(offMs - onMs) < 0.03f)
				{
					break;
				}
				shown++;
				Out($"[Bench] {"",-6}  {name}: {offMs:0.000} -> {onMs:0.000} ms/f");
			}
		}

		static string Pct(float off, float on)
		{
			if (off <= 0f)
			{
				return "n/a";
			}
			float pct = (on - off) / off * 100f;
			return (pct >= 0 ? "+" : "") + pct.ToString("0.0") + "% with patches";
		}

		IEnumerator SetPatches(bool on)
		{
			bool current = Harmony.HasAnyPatches("walle.perf");
			if (current != on)
			{
				Exec(on ? "walleperf on" : "walleperf off");
				yield return null;
				if (Harmony.HasAnyPatches("walle.perf") != on)
				{
					Out("[Bench] WARNING: could not toggle WallePerf (is the mod installed?) — comparison will be meaningless");
				}
				yield return new WaitForSeconds(1f);
			}
		}

		// ---------------- measurement core ----------------

		IEnumerator Measure(string _label, float _seconds)
		{
			currentLabel = _label;
			duration = _seconds;
			elapsed = 0f;
			warmupFrames = 30;
			frameTimes = new List<float>(Mathf.CeilToInt(_seconds * 300f));
			gcStart = GC.CollectionCount(0);
			heapStart = GC.GetTotalMemory(false);
			BenchProfiler.EnsureApplied();
			BenchProfiler.Reset();
			BenchProfiler.Recording = true;
			measuring = true;
			Out($"[Bench] measuring '{_label}' for {_seconds:0}s...");
			while (measuring)
			{
				yield return null;
			}
			BenchProfiler.Recording = false;
		}

		void Update()
		{
			if (anchorActive)
			{
				EntityPlayerLocal p = GameManager.Instance?.World?.GetPrimaryPlayer();
				if (p != null && (p.position - anchorPos).sqrMagnitude > 4f)
				{
					p.SetPosition(anchorPos);
				}
			}
			if (!measuring)
			{
				return;
			}
			if (warmupFrames > 0)
			{
				warmupFrames--;
				return;
			}
			float dt = Time.unscaledDeltaTime;
			frameTimes.Add(dt);
			elapsed += dt;
			if (elapsed >= duration)
			{
				lastResult = BuildResult();
				measuring = false;
			}
		}

		Result BuildResult()
		{
			int n = frameTimes.Count;
			var r = new Result { label = currentLabel, perfOn = Harmony.HasAnyPatches("walle.perf"), frames = n, zombies = CountZombies() };
			if (n < 10)
			{
				return r;
			}
			float sum = 0f, worst = 0f;
			for (int i = 0; i < n; i++)
			{
				sum += frameTimes[i];
				if (frameTimes[i] > worst)
				{
					worst = frameTimes[i];
				}
			}
			List<float> sorted = new List<float>(frameTimes);
			sorted.Sort();
			r.seconds = sum;
			r.avgMs = sum / n * 1000f;
			r.avgFps = n / sum;
			r.p99Ms = sorted[Mathf.Clamp(Mathf.CeilToInt(n * 0.99f) - 1, 0, n - 1)] * 1000f;
			r.onePctLowFps = 1000f / r.p99Ms;
			r.maxMs = worst * 1000f;
			r.minFps = 1f / worst;
			r.gcCollections = GC.CollectionCount(0) - gcStart;
			r.heapDeltaMb = (GC.GetTotalMemory(false) - heapStart) / (1024f * 1024f);
			foreach (BenchProfiler.Slot slot in BenchProfiler.Slots)
			{
				if (slot.calls > 0)
				{
					r.subsystems.Add(new SubsystemRow
					{
						name = slot.name,
						msPerFrame = BenchProfiler.TicksToMs(slot.ticks) / n,
						callsPerFrame = (float)slot.calls / n
					});
				}
			}
			r.subsystems.Sort((a, b) => b.msPerFrame.CompareTo(a.msPerFrame));
			return r;
		}

		void Report(Result r)
		{
			Out("[Bench] ---- " + r.label + " (patches " + (r.perfOn ? "ON" : "OFF") + ") ----");
			Out($"[Bench] avg {r.avgFps:0.0} FPS ({r.avgMs:0.00} ms) | 1% low {r.onePctLowFps:0.0} FPS (p99 {r.p99Ms:0.00} ms) | worst {r.maxMs:0.0} ms");
			Out($"[Bench] {r.frames} frames / {r.seconds:0.0}s | GC {r.gcCollections} | heap {r.heapDeltaMb:+0.0;-0.0} MB | zombies {r.zombies}");
			int shown = 0;
			foreach (SubsystemRow row in r.subsystems)
			{
				if (shown++ >= 8 || row.msPerFrame < 0.01f)
				{
					break;
				}
				Out($"[Bench]   {row.msPerFrame,7:0.000} ms/f ({row.msPerFrame / r.avgMs * 100f,4:0.0}%)  {row.name}");
			}
			WriteProfileCsv(r);
		}

		void WriteProfileCsv(Result r)
		{
			try
			{
				string path = Path.Combine(Path.GetDirectoryName(CsvPath), "profile_results.csv");
				bool writeHeader = !File.Exists(path);
				using (StreamWriter w = new StreamWriter(path, append: true))
				{
					if (writeHeader)
					{
						w.WriteLine("timestamp,scenario,walleperf,subsystem,msPerFrame,callsPerFrame,totalAvgMs");
					}
					string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
					foreach (SubsystemRow row in r.subsystems)
					{
						w.WriteLine(string.Join(",", new[]
						{
							stamp,
							r.label,
							r.perfOn ? "on" : "off",
							"\"" + row.name + "\"",
							row.msPerFrame.ToString("0.0000", CultureInfo.InvariantCulture),
							row.callsPerFrame.ToString("0.00", CultureInfo.InvariantCulture),
							r.avgMs.ToString("0.000", CultureInfo.InvariantCulture)
						}));
					}
				}
			}
			catch (Exception e)
			{
				Log.Warning("[Bench] could not write profile CSV: " + e.Message);
			}
		}

		void WriteCsv(Result r)
		{
			try
			{
				bool writeHeader = !File.Exists(CsvPath);
				using (StreamWriter w = new StreamWriter(CsvPath, append: true))
				{
					if (writeHeader)
					{
						w.WriteLine("timestamp,scenario,walleperf,frames,seconds,avgFps,onePctLowFps,minFps,avgMs,p99Ms,maxMs,gcCollections,heapDeltaMb,zombies");
					}
					w.WriteLine(string.Join(",", new[]
					{
						DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
						r.label,
						r.perfOn ? "on" : "off",
						r.frames.ToString(CultureInfo.InvariantCulture),
						r.seconds.ToString("0.0", CultureInfo.InvariantCulture),
						r.avgFps.ToString("0.00", CultureInfo.InvariantCulture),
						r.onePctLowFps.ToString("0.00", CultureInfo.InvariantCulture),
						r.minFps.ToString("0.00", CultureInfo.InvariantCulture),
						r.avgMs.ToString("0.000", CultureInfo.InvariantCulture),
						r.p99Ms.ToString("0.000", CultureInfo.InvariantCulture),
						r.maxMs.ToString("0.000", CultureInfo.InvariantCulture),
						r.gcCollections.ToString(CultureInfo.InvariantCulture),
						r.heapDeltaMb.ToString("0.0", CultureInfo.InvariantCulture),
						r.zombies.ToString(CultureInfo.InvariantCulture)
					}));
				}
			}
			catch (Exception e)
			{
				Log.Warning("[Bench] could not write CSV: " + e.Message);
			}
		}

		// ---------------- zombies ----------------

		// Deterministic ring at fixed radius/angles: both A/B segments get identical spawns.
		public int SpawnRing(World world, EntityPlayerLocal player, int count)
		{
			int classId = FindZombieClassId();
			if (classId == 0)
			{
				Out("[Bench] no zombie entity class found");
				return 0;
			}
			int spawned = 0;
			const float radius = 22f;
			for (int i = 0; i < count; i++)
			{
				float angle = (float)i / count * Mathf.PI * 2f;
				float x = anchorPos.x + Mathf.Cos(angle) * radius;
				float z = anchorPos.z + Mathf.Sin(angle) * radius;
				float y = world.GetHeightAt(x, z) + 1.2f;
				Entity entity = EntityFactory.CreateEntity(classId, new Vector3(x, y, z));
				if (entity == null)
				{
					continue;
				}
				world.SpawnEntityInWorld(entity);
				spawnedEntityIds.Add(entity.entityId);
				if (entity is EntityAlive alive)
				{
					alive.SetAttackTarget(player, 2400);
				}
				spawned++;
			}
			return spawned;
		}

		public void DespawnAll()
		{
			World world = GameManager.Instance?.World;
			if (world == null)
			{
				spawnedEntityIds.Clear();
				return;
			}
			int removed = 0;
			for (int i = 0; i < spawnedEntityIds.Count; i++)
			{
				if (world.GetEntity(spawnedEntityIds[i]) != null)
				{
					world.RemoveEntity(spawnedEntityIds[i], EnumRemoveEntityReason.Despawned);
					removed++;
				}
			}
			spawnedEntityIds.Clear();
			if (removed > 0)
			{
				Out($"[Bench] despawned {removed} benchmark zombies");
			}
		}

		static int FindZombieClassId()
		{
			int classId = EntityClass.FromString("zombieBoe");
			if (classId != 0 && EntityClass.list.ContainsKey(classId))
			{
				return classId;
			}
			foreach (KeyValuePair<int, EntityClass> kv in EntityClass.list.Dict)
			{
				if (kv.Value.userSpawnType != EntityClass.UserSpawnType.None && kv.Value.entityClassName.StartsWith("zombie"))
				{
					return kv.Key;
				}
			}
			return 0;
		}

		static int CountZombies()
		{
			World world = GameManager.Instance?.World;
			if (world == null)
			{
				return 0;
			}
			int count = 0;
			List<EntityAlive> alives = world.EntityAlives;
			for (int i = 0; i < alives.Count; i++)
			{
				if (alives[i] is EntityEnemy enemy && enemy.IsAlive())
				{
					count++;
				}
			}
			return count;
		}

		static void Exec(string command)
		{
			SingletonMonoBehaviour<SdtdConsole>.Instance.ExecuteSync(command, null);
		}

		static void Out(string msg)
		{
			Log.Out(msg);
			SingletonMonoBehaviour<SdtdConsole>.Instance?.Output(msg);
		}
	}
}

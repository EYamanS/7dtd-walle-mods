using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;

namespace WalleBench
{
	// A Harmony-based sampling profiler for the game's C# subsystems. The retail game is not
	// a development build, so the Unity Profiler can't attach — instead we wrap known hot
	// entry points with stopwatches and report inclusive ms/frame per subsystem. Whatever the
	// table can't account for is engine-side work (rendering, animation, physics, GPU wait).
	public static class BenchProfiler
	{
		public class Slot
		{
			public string name;
			public long ticks;
			public long calls;
		}

		// (display name, declaring type name, method name) — resolved leniently; missing
		// targets are skipped with a log line so game updates can't break the profiler.
		static readonly (string name, string type, string method)[] Targets =
		{
			("Main loop total (gmUpdate)", "GameManager", "gmUpdate"),
			("Entity ticking (World.TickEntities)", "World", "TickEntities"),
			("Entity tick slices (TickEntitiesSlice)", "World", "TickEntitiesSlice"),
			("AI decisions (EAIManager.Update)", "EAIManager", "Update"),
			("Zombie movement+raycasts (UpdateMoveHelper)", "EntityMoveHelper", "UpdateMoveHelper"),
			("Path smoothing (ASPPathFinder.OnPathFinished)", "GamePath.ASPPathFinder", "OnPathFinished"),
			("Chunk mesh upload (CopyChunksToUnity)", "ChunkManager", "CopyChunksToUnity"),
			("Occlusion boxes (RenderOccludees)", "OcclusionManager", "RenderOccludees"),
			("UI (XUi.OnUpdateDeltaTime)", "XUi", "OnUpdateDeltaTime"),
			("Audio (Manager.FrameUpdate)", "Audio.Manager", "FrameUpdate"),
			("Distant POI meshes (DynamicMeshManager.Update)", "DynamicMeshManager", "Update"),
			("Weather (WeatherManager.FrameUpdate)", "WeatherManager", "FrameUpdate"),
			("Nav icons (NavObjectManager.Update)", "NavObjectManager", "Update"),
			("Chunk load mgmt (DetermineChunksToLoad)", "ChunkManager", "DetermineChunksToLoad"),
		};

		static readonly Dictionary<MethodBase, Slot> slotMap = new Dictionary<MethodBase, Slot>();
		public static readonly List<Slot> Slots = new List<Slot>();
		public static bool Recording;

		static Harmony harmony;
		static bool applied;

		public static void EnsureApplied()
		{
			if (applied)
			{
				return;
			}
			harmony = new Harmony("walle.bench.profiler");
			MethodInfo prefix = typeof(BenchProfiler).GetMethod(nameof(TimingPrefix));
			MethodInfo postfix = typeof(BenchProfiler).GetMethod(nameof(TimingPostfix));
			foreach ((string name, string typeName, string methodName) in Targets)
			{
				try
				{
					Type type = AccessTools.TypeByName(typeName);
					MethodInfo target = type == null ? null : AccessTools.Method(type, methodName);
					if (target == null)
					{
						Log.Out("[Bench] profiler: '{0}' not found, skipping", name);
						continue;
					}
					Slot slot = new Slot { name = name };
					slotMap[target] = slot;
					Slots.Add(slot);
					harmony.Patch(target, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
				}
				catch (Exception e)
				{
					Log.Out("[Bench] profiler: could not hook '{0}': {1}", name, e.Message);
				}
			}
			applied = true;
			Log.Out("[Bench] profiler instrumented {0} subsystems", Slots.Count);
		}

		public static void Reset()
		{
			foreach (Slot slot in Slots)
			{
				slot.ticks = 0;
				slot.calls = 0;
			}
		}

		public static void TimingPrefix(out long __state)
		{
			__state = Recording ? Stopwatch.GetTimestamp() : 0L;
		}

		public static void TimingPostfix(long __state, MethodBase __originalMethod)
		{
			if (!Recording || __state == 0L)
			{
				return;
			}
			if (slotMap.TryGetValue(__originalMethod, out Slot slot))
			{
				slot.ticks += Stopwatch.GetTimestamp() - __state;
				slot.calls++;
			}
		}

		public static float TicksToMs(long ticks)
		{
			return (float)((double)ticks / Stopwatch.Frequency * 1000.0);
		}
	}
}

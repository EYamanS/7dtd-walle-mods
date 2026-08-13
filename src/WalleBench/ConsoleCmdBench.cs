using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

namespace WalleBench
{
	// Console command "bench": repeatable benchmark scenarios for A/B testing.
	//   bench base [seconds=30]           - measure the current scene as-is
	//   bench horde [count=40] [sec=45]   - spawn zombies aggroed on you, then measure
	//   bench clear                       - despawn benchmark zombies early
	[Preserve]
	public class ConsoleCmdBench : ConsoleCmdAbstract
	{
		public override string[] getCommands()
		{
			return new string[1] { "bench" };
		}

		public override string getHelp()
		{
			return "bench auto [baseSec=30] [hordeCount=60] [hordeSec=40] - THE one command: god mode, position lock,\n" +
				"  vsync off, fixed time, then base+horde each with patches on/off, restores everything, prints comparison.\n" +
				"bench profile [seconds=15] - subsystem profiler: ranked ms/frame table of where C# main-thread time goes.\n" +
				"bench base [seconds=30] | bench horde [count=40] [seconds=45] | bench clear - manual pieces.\n" +
				"Results append to bench_results.csv in the WalleBench mod folder.";
		}

		public override string getDescription()
		{
			return "WalleBench: repeatable performance benchmark (base / horde scenarios)";
		}

		public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
		{
			World world = GameManager.Instance.World;
			SdtdConsole console = SingletonMonoBehaviour<SdtdConsole>.Instance;
			if (world == null)
			{
				console.Output("[Bench] no game running");
				return;
			}
			EntityPlayerLocal player = world.GetPrimaryPlayer();
			if (player == null)
			{
				console.Output("[Bench] needs a local player (run from the in-game console, not a dedicated server)");
				return;
			}
			Mod mod = ModManager.GetMod("WalleBench");
			BenchRunner.Ensure(mod != null ? mod.Path : System.IO.Path.GetTempPath());
			BenchRunner runner = BenchRunner.Instance;

			string sub = _params.Count > 0 ? _params[0].ToLowerInvariant() : "";
			if (runner.IsBusy && sub != "clear")
			{
				console.Output("[Bench] already running");
				return;
			}
			switch (sub)
			{
				case "auto":
				{
					float baseSec = ParseFloat(_params, 1, 30f);
					int hordeCount = Utils.FastClamp((int)ParseFloat(_params, 2, 60f), 1, 120);
					float hordeSec = ParseFloat(_params, 3, 40f);
					runner.BeginAuto(baseSec, hordeCount, hordeSec);
					break;
				}
				case "base":
				{
					float seconds = ParseFloat(_params, 1, 30f);
					runner.BeginManual("base", seconds);
					break;
				}
				case "horde":
				{
					int count = Utils.FastClamp((int)ParseFloat(_params, 1, 40f), 1, 120);
					float seconds = ParseFloat(_params, 2, 45f);
					int spawned = SpawnHorde(world, player, count, runner.spawnedEntityIds);
					console.Output($"[Bench] spawned {spawned}/{count} zombies around you");
					runner.BeginManual($"horde{spawned}", seconds);
					break;
				}
				case "profile":
				{
					float seconds = ParseFloat(_params, 1, 15f);
					runner.BeginProfile(seconds);
					break;
				}
				case "clear":
					runner.DespawnAll();
					break;
				default:
					console.Output(getHelp());
					break;
			}
		}

		static float ParseFloat(List<string> _params, int index, float fallback)
		{
			if (_params.Count > index && float.TryParse(_params[index], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) && v > 0f)
			{
				return v;
			}
			return fallback;
		}

		static int SpawnHorde(World world, EntityPlayerLocal player, int count, List<int> spawnedIds)
		{
			int classId = FindZombieClassId();
			if (classId == 0)
			{
				SingletonMonoBehaviour<SdtdConsole>.Instance.Output("[Bench] no zombie entity class found");
				return 0;
			}
			int spawned = 0;
			for (int i = 0; i < count; i++)
			{
				if (!world.FindRandomSpawnPointNearPlayer(player, 15, out var x, out var y, out var z, 10))
				{
					continue;
				}
				Entity entity = EntityFactory.CreateEntity(classId, new Vector3(x, (float)y + 0.3f, z));
				if (entity == null)
				{
					continue;
				}
				world.SpawnEntityInWorld(entity);
				spawnedIds.Add(entity.entityId);
				if (entity is EntityAlive alive)
				{
					// aggro immediately so every run applies the same AI load
					alive.SetAttackTarget(player, 2400);
				}
				spawned++;
			}
			return spawned;
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
	}
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using HarmonyLib;

namespace WallePerf
{
	public class WallePerfMod : IModApi
	{
		public const string HarmonyId = "walle.perf";

		public static readonly (string name, Type type)[] AllPatches =
		{
			("MusicThreatCache", typeof(Patches.MusicThreatCache)),
			("WeatherCastDistance", typeof(Patches.WeatherCastDistance)),
			("UiBindingSkip", typeof(Patches.UiBindingSkip)),
			("NavThrottle", typeof(Patches.NavThrottle)),
			("DistantMoveThrottle", typeof(Patches.DistantMoveThrottle)),
			("EntityActivityThrottle", typeof(Patches.EntityActivityThrottle)),
			("MeshValidateSkip", typeof(Patches.MeshValidateSkip)),
			("ExplodeEarlyOut", typeof(Patches.ExplodeEarlyOut)),
			("RallyFlagsScratch", typeof(Patches.RallyFlagsScratch)),
			("AnimatorAudioScratch", typeof(Patches.AnimatorAudioScratch)),
			("CharacterLodCache", typeof(Patches.CharacterLodCache)),
			// Tier 2 (v0.2)
			("PathRequestThrottle", typeof(Patches.PathRequestThrottle)),
			("PathSmoothDistanceCap", typeof(Patches.PathSmoothDistanceCap)),
			("BlockRebuildBudget", typeof(Patches.BlockRebuildBudget)),
			("TerrainUvMetricsSkip", typeof(Patches.TerrainUvMetricsSkip)),
			("TerrainTangentSkip", typeof(Patches.TerrainTangentSkip)),
			("EffectTagCache", typeof(Patches.EffectTagCache)),
			("CompassThrottle", typeof(Patches.CompassThrottle)),
			("StatBarHiddenSkip", typeof(Patches.StatBarHiddenSkip)),
			("CrosshairCameraFix", typeof(Patches.CrosshairCameraFix)),
			("DodgeGate", typeof(Patches.DodgeGate)),
			// Tier 3 engine-load shedding (v0.3)
			("ZombieShadowCull", typeof(Patches.ZombieShadowCull)),
			("ZombieAnimatorCull", typeof(Patches.ZombieAnimatorCull)),
		};

		// Patches that are off unless explicitly enabled in config (experimental).
		static readonly HashSet<string> DefaultOff = new HashSet<string> { "TerrainTangentSkip" };

		static Harmony harmony;
		static HashSet<string> disabledByConfig = new HashSet<string>();
		static HashSet<string> explicitlyEnabled = new HashSet<string>();
		public static bool PatchesActive { get; private set; }

		public void InitMod(Mod _modInstance)
		{
			disabledByConfig = LoadDisabled(_modInstance);
			harmony = new Harmony(HarmonyId);
			ApplyPatches();
			Log.Out("[WallePerf] v0.3.3 loaded ('walleperf off/on/status' in console toggles patches at runtime)");
		}

		public static int ApplyPatches()
		{
			if (PatchesActive)
			{
				return 0;
			}
			int applied = 0;
			foreach ((string name, Type type) in AllPatches)
			{
				if (disabledByConfig.Contains(name))
				{
					Log.Out("[WallePerf] {0}: disabled by config", name);
					continue;
				}
				if (DefaultOff.Contains(name) && !explicitlyEnabled.Contains(name))
				{
					Log.Out("[WallePerf] {0}: experimental, off by default (enable in WallePerfConfig.xml)", name);
					continue;
				}
				try
				{
					harmony.CreateClassProcessor(type).Patch();
					applied++;
					Log.Out("[WallePerf] {0}: applied", name);
				}
				catch (Exception e)
				{
					Log.Error("[WallePerf] {0}: FAILED to apply, skipping this patch", name);
					Log.Exception(e);
				}
			}
			PatchesActive = true;
			Log.Out("[WallePerf] {0}/{1} patches active", applied, AllPatches.Length);
			return applied;
		}

		public static void RemovePatches()
		{
			if (!PatchesActive)
			{
				return;
			}
			harmony.UnpatchSelf();
			PatchesActive = false;
			Log.Out("[WallePerf] all patches removed (vanilla behavior restored)");
		}

		static HashSet<string> LoadDisabled(Mod _modInstance)
		{
			HashSet<string> disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				string path = Path.Combine(_modInstance.Path, "WallePerfConfig.xml");
				if (!File.Exists(path))
				{
					return disabled;
				}
				XmlDocument doc = new XmlDocument();
				doc.Load(path);
				foreach (XmlNode node in doc.SelectNodes("//patch"))
				{
					string name = node.Attributes?["name"]?.Value;
					string enabled = node.Attributes?["enabled"]?.Value;
					if (name == null)
					{
						continue;
					}
					if (string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase))
					{
						disabled.Add(name);
					}
					else if (string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
					{
						explicitlyEnabled.Add(name);
					}
				}
			}
			catch (Exception e)
			{
				Log.Warning("[WallePerf] Could not read WallePerfConfig.xml, all patches enabled");
				Log.Exception(e);
			}
			return disabled;
		}
	}
}

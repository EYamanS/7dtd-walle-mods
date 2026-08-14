using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using HarmonyLib;

namespace WalleQoL
{
	public class WalleQoLMod : IModApi
	{
		public const string HarmonyId = "walle.qol";

		static readonly (string name, Type[] types)[] Features =
		{
			("SharedContainers", new[]
			{
				typeof(Patches.SharedLockPatch),
				typeof(Patches.WorkstationSharedLockPatch),
				typeof(Patches.EntityBagSharedLockPatch),
				typeof(Patches.DestroyOnCloseGuardPatch),
				typeof(Patches.LiveContainerSyncPatch),
			}),
			("QuickDeposit", new[] { typeof(Patches.DepositCommandPatch), typeof(Patches.DepositEnablePatch), typeof(Patches.DepositActivatePatch) }),
			("CraftFromContainers", new[]
			{
				typeof(Patches.CfcScopeCraftHasItems),
				typeof(Patches.CfcScopeCraftActivate),
				typeof(Patches.CfcScopeCraftCount),
				typeof(Patches.CfcScopeIngredientEntry),
				typeof(Patches.CfcScopeRecipeTracker),
				typeof(Patches.CfcScopeRepairRefresh),
				typeof(Patches.CfcScopeRepairActivate),
				typeof(Patches.CfcRecipeListAugment),
				typeof(Patches.CfcAllStacksAugment),
				typeof(Patches.CfcItemCountAugment),
				typeof(Patches.CfcHasItemsAugment),
				typeof(Patches.CfcRemoveItemsAugment),
			}),
		};

		public void InitMod(Mod _modInstance)
		{
			HashSet<string> disabled = LoadDisabled(_modInstance);
			Harmony harmony = new Harmony(HarmonyId);
			foreach ((string name, Type[] types) in Features)
			{
				if (disabled.Contains(name))
				{
					Log.Out("[WalleQoL] {0}: disabled by config", name);
					continue;
				}
				try
				{
					foreach (Type type in types)
					{
						harmony.CreateClassProcessor(type).Patch();
					}
					Log.Out("[WalleQoL] {0}: enabled", name);
				}
				catch (Exception e)
				{
					Log.Error("[WalleQoL] {0}: FAILED to apply", name);
					Log.Exception(e);
				}
			}
			Log.Out("[WalleQoL] v0.3.0 loaded");
		}

		static bool ReadFlag(XmlNode node, string attribute, bool fallback)
		{
			string value = node.Attributes?[attribute]?.Value;
			if (bool.TryParse(value, out bool parsed))
			{
				return parsed;
			}
			return fallback;
		}

		static HashSet<string> LoadDisabled(Mod _modInstance)
		{
			HashSet<string> disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				string path = Path.Combine(_modInstance.Path, "WalleQoLConfig.xml");
				if (!File.Exists(path))
				{
					return disabled;
				}
				XmlDocument doc = new XmlDocument();
				doc.Load(path);
				foreach (XmlNode node in doc.SelectNodes("//feature"))
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
					if (string.Equals(name, "SharedContainers", StringComparison.OrdinalIgnoreCase))
					{
						Patches.SharedConfig.PlayerStorage = ReadFlag(node, "playerStorage", Patches.SharedConfig.PlayerStorage);
						Patches.SharedConfig.WorldLoot = ReadFlag(node, "worldLoot", Patches.SharedConfig.WorldLoot);
						Patches.SharedConfig.Workstations = ReadFlag(node, "workstations", Patches.SharedConfig.Workstations);
						Patches.SharedConfig.DroppedBags = ReadFlag(node, "droppedBags", Patches.SharedConfig.DroppedBags);
					}
					if (string.Equals(name, "CraftFromContainers", StringComparison.OrdinalIgnoreCase))
					{
						string range = node.Attributes?["range"]?.Value;
						if (int.TryParse(range, out int parsedRange) && parsedRange > 0)
						{
							Patches.CfcConfig.Range = Math.Min(parsedRange, 50);
						}
						Patches.CfcConfig.Repair = ReadFlag(node, "repair", Patches.CfcConfig.Repair);
					}
				}
			}
			catch (Exception e)
			{
				Log.Warning("[WalleQoL] Could not read WalleQoLConfig.xml, all features enabled");
				Log.Exception(e);
			}
			return disabled;
		}
	}
}

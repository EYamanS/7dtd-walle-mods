using System;
using System.Collections.Generic;
using HarmonyLib;
using Platform;
using UnityEngine;

namespace WalleQoL.Patches
{
	// ============================== Craft From Containers ==============================
	// Crafting (and optionally item repair) pulls ingredients from nearby PLAYER-PLACED
	// storage. Consumption order: backpack -> toolbelt -> containers (your on-hand mats
	// are used first). Respects container locks (no pulling from boxes you can't open)
	// and the container's own user-locked slots (reserved stacks are never touched).
	//
	// SAFETY DESIGN — scoped augmentation: the low-level XUiM_PlayerInventory methods
	// (GetItemCount/HasItems/RemoveItems/GetAllItemStacks) only see container contents
	// while execution is inside a known crafting/repair call chain (tracked by a scope
	// counter set from prefix/finalizer pairs on those entry points). Dangerous callers
	// that share the same APIs — trader currency (free-goods exploit), vending rent,
	// lockpick consumption, the ammo radial — never enter the scope and always see the
	// real player inventory. Quest turn-ins / challenges / ammo HUD use Bag/Inventory
	// directly and are untouched by design.

	public static class CfcConfig
	{
		public static int Range = 15;      // meters
		public static bool Repair = true;  // item repair may consume repair mats from storage
	}

	public static class Cfc
	{
		static int scopeDepth;
		public static bool Active => scopeDepth > 0;
		public static void Enter() { scopeDepth++; }
		public static void Exit() { if (scopeDepth > 0) { scopeDepth--; } }

		// ---- nearby-container scan, cached briefly (crafting UI hits these APIs in bursts) ----
		static float lastScanTime = -99f;
		static readonly List<TEFeatureStorage> nearby = new List<TEFeatureStorage>();

		public static List<TEFeatureStorage> GetNearby()
		{
			float now = Time.unscaledTime;
			if (now - lastScanTime < 0.5f)
			{
				return nearby;
			}
			lastScanTime = now;
			nearby.Clear();
			World world = GameManager.Instance?.World;
			EntityPlayerLocal player = world?.GetPrimaryPlayer();
			if (player == null)
			{
				return nearby;
			}
			PlatformUserIdentifierAbs self = PlatformManager.InternalLocalUserIdentifier;
			Vector3i blockPos = player.GetBlockPosition();
			int chunkX = World.toChunkXZ(blockPos.x);
			int chunkZ = World.toChunkXZ(blockPos.z);
			int chunkRange = CfcConfig.Range / 16 + 1;
			float rangeSq = (float)CfcConfig.Range * CfcConfig.Range;
			for (int i = -chunkRange; i <= chunkRange; i++)
			{
				for (int j = -chunkRange; j <= chunkRange; j++)
				{
					Chunk chunk = (Chunk)world.GetChunkSync(chunkX + j, chunkZ + i);
					if (chunk == null)
					{
						continue;
					}
					var tileEntities = chunk.GetTileEntities();
					for (int k = 0; k < tileEntities.list.Count; k++)
					{
						TileEntity te = tileEntities.list[k];
						if (!(te is TileEntityComposite composite))
						{
							continue;
						}
						TEFeatureStorage storage = composite.GetFeature<TEFeatureStorage>();
						if (storage == null || !storage.bPlayerStorage || storage.isJammed)
						{
							continue;
						}
						if (storage.lockFeature != null && storage.lockFeature.IsLocked() && !storage.lockFeature.IsUserAllowed(self))
						{
							continue;
						}
						if ((te.ToWorldPos().ToVector3() - player.position).sqrMagnitude > rangeSq)
						{
							continue;
						}
						nearby.Add(storage);
					}
				}
			}
			return nearby;
		}

		// ---- pull rules ----

		static bool HasMods(ItemValue itemValue)
		{
			ItemValue[] mods = itemValue.Modifications;
			if (mods == null)
			{
				return false;
			}
			for (int i = 0; i < mods.Length; i++)
			{
				if (mods[i] != null && mods[i].type != 0)
				{
					return true;
				}
			}
			return false;
		}

		static bool IsPullable(TEFeatureStorage storage, int slot, ItemStack stack)
		{
			if (stack == null || stack.count <= 0 || stack.itemValue.type == 0 || HasMods(stack.itemValue))
			{
				return false;
			}
			PackedBoolArray locks = storage.SlotLocks;
			if (locks != null && slot < locks.Length && locks[slot])
			{
				return false; // user-locked container slot: reserved, never auto-consumed
			}
			return true;
		}

		public static int CountOf(ItemValue itemValue)
		{
			if (itemValue == null || itemValue.type == 0)
			{
				return 0;
			}
			int total = 0;
			List<TEFeatureStorage> containers = GetNearby();
			for (int c = 0; c < containers.Count; c++)
			{
				ItemStack[] items = containers[c].items;
				for (int s = 0; s < items.Length; s++)
				{
					if (IsPullable(containers[c], s, items[s]) && items[s].itemValue.type == itemValue.type)
					{
						total += items[s].count;
					}
				}
			}
			return total;
		}

		// Append pullable container stacks to a stack list used for recipe availability /
		// max-craft-count math (clones, so callers can never mutate chest contents).
		public static void AppendStacks(List<ItemStack> target)
		{
			List<TEFeatureStorage> containers = GetNearby();
			for (int c = 0; c < containers.Count; c++)
			{
				ItemStack[] items = containers[c].items;
				for (int s = 0; s < items.Length; s++)
				{
					if (IsPullable(containers[c], s, items[s]))
					{
						target.Add(items[s].Clone());
					}
				}
			}
		}

		// Drain up to `needed` of an item from nearby containers. Returns amount taken.
		// One batched SetModified per touched chest (single net package, live-refreshes
		// any open windows via the tile-entity listeners).
		public static int TakeFrom(ItemValue itemValue, int needed, IList<ItemStack> removedItems)
		{
			int taken = 0;
			List<TEFeatureStorage> containers = GetNearby();
			for (int c = 0; c < containers.Count && taken < needed; c++)
			{
				TEFeatureStorage storage = containers[c];
				ItemStack[] items = storage.items;
				bool changed = false;
				storage.Parent.SetDisableModifiedCheck(true);
				try
				{
					for (int s = 0; s < items.Length && taken < needed; s++)
					{
						if (!IsPullable(storage, s, items[s]) || items[s].itemValue.type != itemValue.type)
						{
							continue;
						}
						int take = Math.Min(items[s].count, needed - taken);
						items[s].count -= take;
						taken += take;
						changed = true;
						removedItems?.Add(new ItemStack(items[s].itemValue.Clone(), take));
						if (items[s].count <= 0)
						{
							items[s] = ItemStack.Empty;
						}
					}
				}
				finally
				{
					storage.Parent.SetDisableModifiedCheck(false);
				}
				if (changed)
				{
					storage.SetModified();
				}
			}
			return taken;
		}

		// "-N item" entry in the pickup feed for the container-drawn part, so players see
		// what crafting pulled from storage.
		public static void ShowTakenToast(EntityPlayerLocal player, ItemValue itemValue, int count)
		{
			if (player == null || count <= 0)
			{
				return;
			}
			player.PlayerUI?.xui?.CollectedItemList?.RemoveItemStack(new ItemStack(itemValue.Clone(), count));
		}
	}

	// ---------------- scope entry points (crafting + repair call chains) ----------------

	[HarmonyPatch(typeof(ItemActionEntryCraft), nameof(ItemActionEntryCraft.hasItems))]
	public static class CfcScopeCraftHasItems
	{
		public static void Prefix() { Cfc.Enter(); }
		public static void Finalizer() { Cfc.Exit(); }
	}

	[HarmonyPatch(typeof(ItemActionEntryCraft), nameof(ItemActionEntryCraft.OnActivated))]
	public static class CfcScopeCraftActivate
	{
		public static void Prefix() { Cfc.Enter(); }
		public static void Finalizer() { Cfc.Exit(); }
	}

	[HarmonyPatch(typeof(XUiC_RecipeCraftCount), "calcMaxCraftable")]
	public static class CfcScopeCraftCount
	{
		public static void Prefix() { Cfc.Enter(); }
		public static void Finalizer() { Cfc.Exit(); }
	}

	[HarmonyPatch(typeof(XUiC_IngredientEntry), "GetBindingValueInternal")]
	public static class CfcScopeIngredientEntry
	{
		public static void Prefix() { Cfc.Enter(); }
		public static void Finalizer() { Cfc.Exit(); }
	}

	[HarmonyPatch(typeof(XUiC_RecipeTrackerIngredientEntry), "GetBindingValueInternal")]
	public static class CfcScopeRecipeTracker
	{
		public static void Prefix() { Cfc.Enter(); }
		public static void Finalizer() { Cfc.Exit(); }
	}

	[HarmonyPatch(typeof(ItemActionEntryRepair), nameof(ItemActionEntryRepair.RefreshEnabled))]
	public static class CfcScopeRepairRefresh
	{
		public static void Prefix() { if (CfcConfig.Repair) { Cfc.Enter(); } }
		public static void Finalizer() { if (CfcConfig.Repair) { Cfc.Exit(); } }
	}

	[HarmonyPatch(typeof(ItemActionEntryRepair), nameof(ItemActionEntryRepair.OnActivated))]
	public static class CfcScopeRepairActivate
	{
		public static void Prefix() { if (CfcConfig.Repair) { Cfc.Enter(); } }
		public static void Finalizer() { if (CfcConfig.Repair) { Cfc.Exit(); } }
	}

	// ---------------- recipe list availability (stack-list based, not count based) ----------------

	// The recipe list computes craftability from a raw stack list. Append container stacks
	// before it is built — except at workstations with an input grid (forge, mixer...),
	// where crafting consumes from the grid and inflating the list would show green
	// recipes the grid can't pay for.
	[HarmonyPatch(typeof(XUiC_RecipeList), nameof(XUiC_RecipeList.BuildRecipeInfosList))]
	public static class CfcRecipeListAugment
	{
		public static void Prefix(XUiC_RecipeList __instance, List<ItemStack> _items)
		{
			if (__instance.windowGroup?.Controller?.GetChildByType<XUiC_WorkstationInputGrid>() != null)
			{
				return;
			}
			Cfc.AppendStacks(_items);
		}
	}

	// ---------------- scoped low-level augmentation ----------------

	[HarmonyPatch(typeof(XUiM_PlayerInventory), nameof(XUiM_PlayerInventory.GetAllItemStacks))]
	public static class CfcAllStacksAugment
	{
		public static void Postfix(List<ItemStack> __result)
		{
			if (Cfc.Active)
			{
				Cfc.AppendStacks(__result);
			}
		}
	}

	[HarmonyPatch(typeof(XUiM_PlayerInventory), nameof(XUiM_PlayerInventory.GetItemCount), typeof(ItemValue))]
	public static class CfcItemCountAugment
	{
		public static void Postfix(ItemValue _itemValue, ref int __result)
		{
			if (Cfc.Active)
			{
				__result += Cfc.CountOf(_itemValue);
			}
		}
	}

	[HarmonyPatch(typeof(XUiM_PlayerInventory), nameof(XUiM_PlayerInventory.HasItems))]
	public static class CfcHasItemsAugment
	{
		public static void Postfix(XUiM_PlayerInventory __instance, IList<ItemStack> _itemStacks, int _multiplier, ref bool __result)
		{
			if (__result || !Cfc.Active)
			{
				return;
			}
			for (int i = 0; i < _itemStacks.Count; i++)
			{
				int need = _itemStacks[i].count * _multiplier;
				need -= __instance.backpack.GetItemCount(_itemStacks[i].itemValue);
				if (need > 0)
				{
					need -= __instance.toolbelt.GetItemCount(_itemStacks[i].itemValue);
				}
				if (need > 0)
				{
					need -= Cfc.CountOf(_itemStacks[i].itemValue);
				}
				if (need > 0)
				{
					return;
				}
			}
			__result = true;
		}
	}

	// Container-aware removal: backpack -> toolbelt -> containers. Replaces the original
	// while in scope (also fixes the vanilla quirk where the availability guard ignored
	// the multiplier). Outside the scope the vanilla method runs untouched.
	[HarmonyPatch(typeof(XUiM_PlayerInventory), nameof(XUiM_PlayerInventory.RemoveItems))]
	public static class CfcRemoveItemsAugment
	{
		public static bool Prefix(XUiM_PlayerInventory __instance, IList<ItemStack> _itemStacks, int _multiplier, IList<ItemStack> _removedItems)
		{
			if (!Cfc.Active)
			{
				return true;
			}
			// availability check including containers (multiplier-correct)
			for (int i = 0; i < _itemStacks.Count; i++)
			{
				int need = _itemStacks[i].count * _multiplier;
				need -= __instance.backpack.GetItemCount(_itemStacks[i].itemValue);
				if (need > 0)
				{
					need -= __instance.toolbelt.GetItemCount(_itemStacks[i].itemValue);
				}
				if (need > 0)
				{
					need -= Cfc.CountOf(_itemStacks[i].itemValue);
				}
				if (need > 0)
				{
					return false; // insufficient: no-op, matching vanilla guard semantics
				}
			}
			for (int i = 0; i < _itemStacks.Count; i++)
			{
				int need = _itemStacks[i].count * _multiplier;
				need -= __instance.backpack.DecItem(_itemStacks[i].itemValue, need, _ignoreModdedItems: true, _removedItems);
				if (need > 0)
				{
					need -= __instance.toolbelt.DecItem(_itemStacks[i].itemValue, need, _ignoreModdedItems: true, _removedItems);
				}
				if (need > 0)
				{
					int taken = Cfc.TakeFrom(_itemStacks[i].itemValue, need, _removedItems);
					Cfc.ShowTakenToast(__instance.localPlayer, _itemStacks[i].itemValue, taken);
				}
			}
			__instance.dispatchBackpackItemsChanged();
			__instance.dispatchToolbeltItemsChanged();
			return false;
		}
	}
}

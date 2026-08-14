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
		public static bool Forge = true;   // forge recipes may consume chest smeltables at material value
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

		// ---------------- forge material support ----------------
		// Forge recipes consume smelted material units (unit_iron, unit_brass...) from the
		// material bank. With Forge enabled, the shortfall can come from raw smeltables in
		// nearby chests at their material value (ItemClass.GetWeight(), the same conversion
		// the smelter itself uses at TileEntityWorkstation.HandleMaterialInput). Whole items
		// are consumed; leftover units are credited INTO the forge bank so nothing is wasted.
		// This intentionally skips the melt timer for the shortfall — the bank is always
		// drained first, so pre-smelted units keep their value.

		// Only genuine unit ingredients trigger material matching; a regular item ingredient
		// that happens to be made of a forgeable material must never be substituted.
		public static string UnitCategoryOf(ItemValue itemValue)
		{
			ItemClass itemClass = itemValue?.ItemClass;
			if (itemClass == null || itemClass.Name == null || !itemClass.Name.StartsWith("unit_"))
			{
				return null;
			}
			return itemClass.MadeOfMaterial?.ForgeCategory;
		}

		static bool IsSmeltableFor(ItemStack stack, string category)
		{
			ItemClass itemClass = stack.itemValue.ItemClass;
			string itemCategory = itemClass?.MadeOfMaterial?.ForgeCategory;
			return itemCategory != null && itemCategory.EqualsCaseInsensitive(category) && itemClass.GetWeight() > 0;
		}

		public static int ChestUnitsOf(string category)
		{
			if (category == null)
			{
				return 0;
			}
			int units = 0;
			List<TEFeatureStorage> containers = GetNearby();
			for (int c = 0; c < containers.Count; c++)
			{
				ItemStack[] items = containers[c].items;
				for (int s = 0; s < items.Length; s++)
				{
					if (IsPullable(containers[c], s, items[s]) && IsSmeltableFor(items[s], category))
					{
						units += items[s].count * items[s].itemValue.ItemClass.GetWeight();
					}
				}
			}
			return units;
		}

		// Virtual unit stacks for the forge recipe list / craft-count math (aggregated per
		// category, using the same "unit_" + category lookup the smelter uses).
		public static void AppendForgeUnitStacks(List<ItemStack> target)
		{
			Dictionary<string, int> unitsByCategory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			List<TEFeatureStorage> containers = GetNearby();
			for (int c = 0; c < containers.Count; c++)
			{
				ItemStack[] items = containers[c].items;
				for (int s = 0; s < items.Length; s++)
				{
					if (!IsPullable(containers[c], s, items[s]))
					{
						continue;
					}
					ItemClass itemClass = items[s].itemValue.ItemClass;
					string category = itemClass?.MadeOfMaterial?.ForgeCategory;
					if (category == null || itemClass.GetWeight() <= 0)
					{
						continue;
					}
					unitsByCategory.TryGetValue(category, out int have);
					unitsByCategory[category] = have + items[s].count * itemClass.GetWeight();
				}
			}
			foreach (var kv in unitsByCategory)
			{
				ItemValue unitItem = ItemClass.GetItem("unit_" + kv.Key);
				if (unitItem != null && unitItem.type != 0)
				{
					target.Add(new ItemStack(unitItem, kv.Value));
				}
			}
		}

		// Drain chest smeltables to cover `needed` units of the given unit ingredient.
		// Whole items are consumed; change is credited to the forge bank via SetWeight.
		public static int TakeForgeUnits(XUiC_WorkstationMaterialInputGrid grid, ItemValue unitItemValue, int needed, IList<ItemStack> removedItems)
		{
			string category = UnitCategoryOf(unitItemValue);
			if (category == null || needed <= 0)
			{
				return 0;
			}
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
						if (!IsPullable(storage, s, items[s]) || !IsSmeltableFor(items[s], category))
						{
							continue;
						}
						int weight = items[s].itemValue.ItemClass.GetWeight();
						int wantItems = (needed - taken + weight - 1) / weight;
						int take = Math.Min(items[s].count, wantItems);
						items[s].count -= take;
						taken += take * weight;
						changed = true;
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
			int used = Math.Min(taken, needed);
			if (taken > needed)
			{
				grid.SetWeight(unitItemValue, taken - needed); // change goes into the bank
			}
			if (used > 0)
			{
				removedItems?.Add(new ItemStack(unitItemValue.Clone(), used));
			}
			return used;
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
	// before it is built. At the forge (material input grid) the list holds unit stacks, so
	// chest smeltables are appended as virtual unit stacks instead; the matching consumption
	// patches below make those recipes actually payable.
	[HarmonyPatch(typeof(XUiC_RecipeList), nameof(XUiC_RecipeList.BuildRecipeInfosList))]
	public static class CfcRecipeListAugment
	{
		public static void Prefix(XUiC_RecipeList __instance, List<ItemStack> _items)
		{
			XUiC_WorkstationInputGrid grid = __instance.windowGroup?.Controller?.GetChildByType<XUiC_WorkstationInputGrid>();
			if (grid != null)
			{
				if (CfcConfig.Forge && grid is XUiC_WorkstationMaterialInputGrid)
				{
					Cfc.AppendForgeUnitStacks(_items);
				}
				return;
			}
			Cfc.AppendStacks(_items);
		}
	}

	// ---------------- forge: craft-enable gate, consumption, max-craft count ----------------

	[HarmonyPatch(typeof(XUiC_WorkstationInputGrid), nameof(XUiC_WorkstationInputGrid.HasItems))]
	public static class CfcForgeHasItemsAugment
	{
		public static void Postfix(XUiC_WorkstationInputGrid __instance, IList<ItemStack> _itemStacks, int _multiplier, ref bool __result)
		{
			if (__result || !CfcConfig.Forge || !Cfc.Active || !(__instance is XUiC_WorkstationMaterialInputGrid))
			{
				return;
			}
			for (int i = 0; i < _itemStacks.Count; i++)
			{
				int need = _itemStacks[i].count * _multiplier - __instance.GetItemCount(_itemStacks[i].itemValue);
				if (need > 0)
				{
					need -= Cfc.ChestUnitsOf(Cfc.UnitCategoryOf(_itemStacks[i].itemValue));
				}
				if (need > 0)
				{
					return;
				}
			}
			__result = true;
		}
	}

	// Consumption: bank units first (vanilla DecItem, keeps TE sync), chest smeltables for
	// the shortfall with change credited back into the bank.
	[HarmonyPatch(typeof(XUiC_WorkstationInputGrid), nameof(XUiC_WorkstationInputGrid.RemoveItems))]
	public static class CfcForgeRemoveItemsAugment
	{
		public static bool Prefix(XUiC_WorkstationInputGrid __instance, IList<ItemStack> _itemStacks, int _multiplier, IList<ItemStack> _removedItems)
		{
			if (!CfcConfig.Forge || !Cfc.Active || !(__instance is XUiC_WorkstationMaterialInputGrid materialGrid))
			{
				return true;
			}
			for (int i = 0; i < _itemStacks.Count; i++)
			{
				int need = _itemStacks[i].count * _multiplier;
				need -= __instance.DecItem(_itemStacks[i].itemValue, need, _removedItems);
				if (need > 0)
				{
					int taken = Cfc.TakeForgeUnits(materialGrid, _itemStacks[i].itemValue, need, _removedItems);
					Cfc.ShowTakenToast(__instance.xui?.playerUI?.entityPlayer, _itemStacks[i].itemValue, taken);
				}
			}
			return false;
		}
	}

	// Max-craft count at the forge: recompute with chest units included (mirrors the vanilla
	// algorithm exactly, including the per-ingredient crafting modifier).
	[HarmonyPatch(typeof(XUiC_RecipeCraftCount), "calcMaxCraftable")]
	public static class CfcForgeCraftCountAugment
	{
		public static void Postfix(XUiC_RecipeCraftCount __instance, ref int __result)
		{
			if (!CfcConfig.Forge)
			{
				return;
			}
			XUiC_WorkstationInputGrid grid = __instance.windowGroup?.Controller?.GetChildByType<XUiC_WorkstationInputGrid>();
			if (!(grid is XUiC_WorkstationMaterialInputGrid))
			{
				return;
			}
			Recipe recipe = __instance.recipe;
			if (recipe == null)
			{
				return;
			}
			ItemStack[] array = grid.GetSlots();
			for (int i = 0; i < recipe.ingredients.Count; i++)
			{
				if (recipe.ingredients[i] != null && recipe.ingredients[i].itemValue.HasQuality)
				{
					return; // vanilla returned 1 for quality recipes; keep it
				}
			}
			int result = int.MaxValue;
			int craftingTier = ((recipe.craftingTier == -1) ? recipe.GetCraftingTier(__instance.xui.playerUI.entityPlayer) : recipe.craftingTier);
			for (int j = 0; j < recipe.ingredients.Count; j++)
			{
				ItemStack ingredient = recipe.ingredients[j];
				if (ingredient == null || ingredient.itemValue.type == 0)
				{
					continue;
				}
				float perCraft;
				if (recipe.UseIngredientModifier)
				{
					perCraft = (int)EffectManager.GetValue(PassiveEffects.CraftingIngredientCount, null, ingredient.count, __instance.xui.playerUI.entityPlayer, recipe, FastTags<TagGroup.Global>.Parse(ingredient.itemValue.ItemClass.GetItemName()), calcEquipment: true, calcHoldingItem: true, calcProgression: true, calcBuffs: true, calcChallenges: true, craftingTier);
					if (perCraft > 0f)
					{
						perCraft = (int)(perCraft * XUiM_Recipes.GetCraftingInputModifier(recipe));
						if (XUiM_Recipes.CraftingInputModifier > 0f)
						{
							perCraft = Utils.FastMax(1f, perCraft);
						}
					}
				}
				else
				{
					perCraft = ingredient.count;
				}
				if (perCraft < 1f)
				{
					continue;
				}
				int available = 0;
				for (int k = 0; k < array.Length; k++)
				{
					if (array[k] != null && array[k].itemValue.type != 0 && ingredient.itemValue.type == array[k].itemValue.type)
					{
						available += array[k].count;
					}
				}
				available += Cfc.ChestUnitsOf(Cfc.UnitCategoryOf(ingredient.itemValue));
				int craftable = Mathf.CeilToInt((float)available / perCraft);
				if (Mathf.FloorToInt(perCraft * (float)craftable) > available)
				{
					craftable--;
				}
				result = Mathf.Min(craftable, result);
				if (result == 0)
				{
					break;
				}
			}
			__result = ((XUiM_Recipes.CraftingInputModifier == 0f) ? 10000 : Mathf.Clamp(result, 1, 10000));
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

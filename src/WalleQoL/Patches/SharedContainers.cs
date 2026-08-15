using HarmonyLib;

namespace WalleQoL.Patches
{
	// Scope toggles for shared access, read from the SharedContainers feature element in
	// WalleQoLConfig.xml (attributes playerStorage/worldLoot/workstations/droppedBags).
	public static class SharedConfig
	{
		public static bool PlayerStorage = true;
		// The three scopes below shipped in 0.2.0 and were reported buggy in real play;
		// they default OFF since 0.4.1 and are opt-in experimental until the underlying
		// sync issues are reproduced and fixed.
		public static bool WorldLoot = false;
		public static bool Workstations = false;
		public static bool DroppedBags = false;
	}

	// The v2.x LockManager natively supports shared locks (that is how several players use
	// one trader at the same time) — most containers just never opt in. These patches opt
	// them in per category. IsSharedLock is evaluated on the server, so the host/server
	// needs this mod.

	// Player-placed storage and world loot (cabinets, cars, crates...). Quest containers
	// (isQuestLoot) stay single-lock: quest objectives track per-player container state.
	// Loot generation is safe under sharing: it is server-only, guarded by bTouched before
	// the roll, and lock requests are processed strictly sequentially (LootManager.cs:22-26)
	// — two simultaneous opens produce exactly one roll.
	[HarmonyPatch(typeof(TEFeatureAbs), nameof(TEFeatureAbs.IsSharedLock))]
	public static class SharedLockPatch
	{
		public static void Postfix(TEFeatureAbs __instance, ref bool __result)
		{
			if (__result || !(__instance is TEFeatureStorage storage))
			{
				return;
			}
			if (storage.bPlayerStorage)
			{
				__result = SharedConfig.PlayerStorage;
			}
			else
			{
				__result = SharedConfig.WorldLoot && !storage.isQuestLoot;
			}
		}
	}

	// Workstations (forge, workbench, chemistry station, cement mixer...). Their read()
	// already accepts slot/queue data while a user is accessing (only local burn-state
	// simulation is gated), so slots live-sync between two open windows natively.
	// Known limit: the crafting queue syncs as one snapshot — two players editing the
	// queue in the same instant is last-write-wins.
	[HarmonyPatch(typeof(TileEntity), nameof(TileEntity.IsSharedLock))]
	public static class WorkstationSharedLockPatch
	{
		public static void Postfix(TileEntity __instance, ref bool __result)
		{
			if (!__result && SharedConfig.Workstations && __instance is TileEntityWorkstation)
			{
				__result = true;
			}
		}
	}

	// Lootable item entities: dropped player backpacks, zombie loot bags, supply crates.
	// These lock on the entity itself (EntityItem requests the lock in its "search"
	// command). Entities with deliberate single-lock overrides (drones, vehicles) declare
	// their own IsSharedLock and are untouched by patching the base declaration.
	[HarmonyPatch(typeof(Entity), nameof(Entity.IsSharedLock))]
	public static class EntityBagSharedLockPatch
	{
		public static void Postfix(Entity __instance, ref bool __result)
		{
			if (!__result && SharedConfig.DroppedBags && __instance is EntityItem)
			{
				__result = true;
			}
		}
	}

	// Destroy-on-close containers (bird nests, trash...): closing triggers
	// CheckDestroyTileEntity, which destroys the block even while another player still has
	// it open (vanilla only guards the content-drop, GameManager.cs:5230 — not the block
	// destruction that follows). Skip the whole close-out while any other player still
	// holds a lock; the last player out triggers it as vanilla intends.
	[HarmonyPatch(typeof(TEFeatureStorage), nameof(TEFeatureStorage.OnUnlockedServer))]
	public static class DestroyOnCloseGuardPatch
	{
		public static bool Prefix(TEFeatureStorage __instance)
		{
			return !LockManager.Instance.IsLockedServer(__instance, 0);
		}
	}

	// TEFeatureStorage.Read deliberately DISCARDS incoming item data while the local player
	// has the container open (vanilla assumes nobody else can be in it). With shared access
	// that would mean the second viewer sees a stale/empty container and the host silently
	// drops guests' changes. Briefly clearing the user-accessing flag makes Read accept the
	// data; the open loot window then refreshes itself through the tile-entity listener
	// (XUiC_LootContainer.OnTileEntityChanged rebinds all slots).
	// We still skip applying while our own optimistic write is in flight
	// (bWaitingForServerResponse) so a stale broadcast can't clobber a just-made local move.
	//
	// IMPORTANT: only applies to containers whose scope is actually SHARED. Vanilla's
	// discard-while-open makes loot windows immune to network races (e.g. a quest
	// container's open-time population broadcast landing after the player already took the
	// quest item — accepting it makes the item reappear in the container: the "can't pick
	// up the quest item" glitch). Unshared containers keep exact vanilla behavior.
	[HarmonyPatch(typeof(TEFeatureStorage), nameof(TEFeatureStorage.Read), typeof(PooledBinaryReader), typeof(TileEntity.StreamModeRead))]
	public static class LiveContainerSyncPatch
	{
		static bool SharingEnabledFor(TEFeatureStorage storage)
		{
			if (storage.bPlayerStorage)
			{
				return SharedConfig.PlayerStorage;
			}
			return SharedConfig.WorldLoot && !storage.isQuestLoot;
		}

		public static void Prefix(TEFeatureStorage __instance, out bool __state)
		{
			__state = false;
			TileEntityComposite parent = __instance.Parent;
			if (parent != null && parent.IsUserAccessing() && !parent.bWaitingForServerResponse && SharingEnabledFor(__instance))
			{
				parent.SetUserAccessing(false);
				__state = true;
			}
		}

		public static void Postfix(TEFeatureStorage __instance, bool __state)
		{
			if (__state)
			{
				__instance.Parent.SetUserAccessing(true);
			}
		}
	}
}

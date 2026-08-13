using HarmonyLib;

namespace WalleQoL.Patches
{
	// SharedContainers 1/2: the v2.x LockManager natively supports shared locks (that is how
	// several players can use one trader at the same time) — storage chests just never opt in.
	// Opt player-placed storage in, so friends can open the same chest simultaneously.
	// World loot stays single-lock on purpose: shared access there would double-roll loot
	// (OnLockedServer -> PopulateTE per player) and lets destroy-on-close containers vanish
	// under the second viewer.
	// NOTE: IsSharedLock is evaluated on the server, so the host/server needs this mod.
	[HarmonyPatch(typeof(TEFeatureAbs), nameof(TEFeatureAbs.IsSharedLock))]
	public static class SharedLockPatch
	{
		public static void Postfix(TEFeatureAbs __instance, ref bool __result)
		{
			if (!__result && __instance is TEFeatureStorage storage && storage.bPlayerStorage)
			{
				__result = true;
			}
		}
	}

	// SharedContainers 2/2: TEFeatureStorage.Read deliberately DISCARDS incoming item data
	// while the local player has the container open (vanilla assumes nobody else can be in
	// it). With shared access that would mean: the host silently drops every change a guest
	// makes while the host has the chest open, and two open windows never see each other's
	// changes until reopen. Briefly clearing the user-accessing flag makes Read accept the
	// data; the open loot window then refreshes itself through the tile-entity listener
	// (XUiC_LootContainer.OnTileEntityChanged rebinds all slots).
	// We still skip applying while our own optimistic write is in flight
	// (bWaitingForServerResponse) so a stale broadcast can't clobber a just-made local move.
	[HarmonyPatch(typeof(TEFeatureStorage), nameof(TEFeatureStorage.Read), typeof(PooledBinaryReader), typeof(TileEntity.StreamModeRead))]
	public static class LiveContainerSyncPatch
	{
		public static void Prefix(TEFeatureStorage __instance, out bool __state)
		{
			__state = false;
			TileEntityComposite parent = __instance.Parent;
			if (parent != null && parent.IsUserAccessing() && __instance.bPlayerStorage && !parent.bWaitingForServerResponse)
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

using HarmonyLib;

namespace WallePerf.Patches
{
	// T2.3: When a block changes near the player, the affected chunks (up to 9) are put on a
	// priority list that CopyChunksToUnity drains in a do/while IN ONE FRAME, each via
	// CreateMeshAll with no time slicing — the classic place/break-a-block hitch. Replace the
	// drain loop with a single pass per frame: priority chunks still rebuild first and use
	// the fast path, but the spike is bounded to one chunk's worth of work per frame
	// (worst case ~9 frames = ~150ms of visual latency instead of one giant hitch).
	[HarmonyPatch(typeof(ChunkManager), nameof(ChunkManager.CopyChunksToUnity))]
	public static class BlockRebuildBudget
	{
		public static bool Prefix(ChunkManager __instance, ref bool __result)
		{
			__result = __instance.doCopyChunksToUnity();
			return false;
		}
	}
}

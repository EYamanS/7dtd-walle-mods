using HarmonyLib;

namespace WallePerf.Patches
{
	// T1.6: World.EntityActivityUpdate runs every game tick (20/s): an O(entities x players)
	// closest-player scan plus a full sort of each player's entity list, to produce AI
	// activity rankings that barely change tick to tick. Every 5th tick (250ms) is plenty
	// for its consumers (aiActiveScale, jiggle/cloth LOD, aiClosestPlayerDistSq).
	[HarmonyPatch(typeof(World), nameof(World.EntityActivityUpdate))]
	public static class EntityActivityThrottle
	{
		const int IntervalTicks = 5;

		static int counter;

		public static bool Prefix()
		{
			return ++counter % IntervalTicks == 0;
		}
	}
}

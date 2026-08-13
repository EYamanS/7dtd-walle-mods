using HarmonyLib;

namespace WallePerf.Patches
{
	// T1.5: The AI activity throttle (aiActiveScale) slows decision-making for distant
	// entities, but EntityMoveHelper still runs obstacle detection at full rate for every
	// spawned entity: a Physics.SphereCast plus 2-3 voxel sphere raycasts every 4 ticks.
	// Stretch that interval for entities farther than 15m from the closest player.
	[HarmonyPatch(typeof(EntityMoveHelper), nameof(EntityMoveHelper.UpdateMoveHelper))]
	public static class DistantMoveThrottle
	{
		const float FarDistSq = 225f; // 15m — matches vanilla's own aiActiveScale distance band
		const int FarCheckTicks = 20; // 1s between obstacle checks when far (vanilla: 4 ticks)

		public static void Prefix(EntityMoveHelper __instance)
		{
			EntityAlive entity = __instance.entity;
			if (entity != null && entity.aiClosestPlayerDistSq > FarDistSq && __instance.obstacleCheckTickDelay <= 1)
			{
				__instance.obstacleCheckTickDelay = FarCheckTicks;
			}
		}
	}
}

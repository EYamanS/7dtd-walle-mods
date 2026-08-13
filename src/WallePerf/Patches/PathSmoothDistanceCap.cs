using GamePath;
using HarmonyLib;

namespace WallePerf.Patches
{
	// T2.2: Every finished path gets corner-smoothed on the main thread with up to 4 physics
	// casts (2 Linecasts + 2 SphereCasts) per corner pair — 50-100+ casts for a long path,
	// per zombie, every ~0.5s while chasing. The smoothing is purely cosmetic (cut corners
	// look nicer); for entities farther than 20m from any player nobody can tell. Returning
	// false = "line not clear" keeps the unsmoothed but fully valid path.
	[HarmonyPatch(typeof(ASPPathFinder), "IsLineClear")]
	public static class PathSmoothDistanceCap
	{
		const float FarDistSq = 400f; // 20m

		public static bool Prefix(ASPPathFinder __instance, ref bool __result)
		{
			EntityAlive entity = __instance.entity;
			if (entity != null && entity.aiClosestPlayerDistSq > FarDistSq)
			{
				__result = false;
				return false;
			}
			return true;
		}
	}
}

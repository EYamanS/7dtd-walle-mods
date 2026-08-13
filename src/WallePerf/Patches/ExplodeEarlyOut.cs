using HarmonyLib;

namespace WallePerf.Patches
{
	// T1.8a: ExplodeGroupFrameUpdate resolves the "fallingBlock" entity class (string hash
	// lookup) every frame even when no explosion is being processed, which is almost always.
	[HarmonyPatch(typeof(GameManager), "ExplodeGroupFrameUpdate")]
	public static class ExplodeEarlyOut
	{
		public static bool Prefix(GameManager __instance)
		{
			return __instance.explodeFallingGroups.Count > 0;
		}
	}
}

using HarmonyLib;
using UnityEngine;

namespace WallePerf.Patches
{
	// T1.4: NavObjectManager.Update re-validates every compass/map icon against all of its
	// nav classes every frame (CVar dictionary lookups + full EffectManager stack walks).
	// Icon validity does not need 60Hz — run the whole update at ~12Hz.
	[HarmonyPatch(typeof(NavObjectManager), nameof(NavObjectManager.Update))]
	public static class NavThrottle
	{
		const int IntervalFrames = 5;

		public static bool Prefix()
		{
			return Time.frameCount % IntervalFrames == 0;
		}
	}
}

using HarmonyLib;
using UnityEngine;

namespace WallePerf.Patches
{
	// T2.6a: The compass rebuilds every marker category (sleeping bags, land claims, quests,
	// treasure, vending, air drops, ...) and refreshes all its bindings — with several full
	// effect-stack walks — every single frame. 10Hz is indistinguishable on a compass strip.
	[HarmonyPatch(typeof(XUiC_CompassWindow), nameof(XUiC_CompassWindow.Update))]
	public static class CompassThrottle
	{
		const int IntervalFrames = 6;

		public static bool Prefix()
		{
			return Time.frameCount % IntervalFrames == 0;
		}
	}
}

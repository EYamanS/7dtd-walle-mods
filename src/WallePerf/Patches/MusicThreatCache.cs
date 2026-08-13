using DynamicMusic;
using HarmonyLib;
using UnityEngine;

namespace WallePerf.Patches
{
	// T1.1: GetThreatLevelOn does a 50m GetEntitiesInBounds sweep (49 chunk lookups through a
	// RW-lock + reflection type checks) every single frame, just to pick background music.
	// Recompute at 10Hz and serve a cached value in between.
	[HarmonyPatch(typeof(ThreatLevelUtility), nameof(ThreatLevelUtility.GetThreatLevelOn))]
	public static class MusicThreatCache
	{
		const int IntervalFrames = 6;
		// Vanilla averages the last 300 per-frame samples during blood moon (~5s at 60fps).
		// At 10Hz the same 5s window is ~50 samples, so trim the queue to keep music behavior.
		const int QueueCap = 50;

		static float cachedResult;
		static int nextComputeFrame = -1;

		public static bool Prefix(ref float __result)
		{
			if (Time.frameCount < nextComputeFrame)
			{
				__result = cachedResult;
				return false;
			}
			nextComputeFrame = Time.frameCount + IntervalFrames;
			return true;
		}

		public static void Postfix(float __result)
		{
			cachedResult = __result;
			var queue = ThreatLevelUtility.threatLevels;
			while (queue.Count > QueueCap)
			{
				queue.Dequeue();
			}
		}
	}
}

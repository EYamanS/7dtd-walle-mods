using System.Collections.Generic;
using GamePath;
using HarmonyLib;
using UnityEngine;

namespace WallePerf.Patches
{
	// T2.1: Chasing AI re-requests an A* path every 0.3-0.8s per entity even when the target
	// barely moved — and immediately when the current path is nearly consumed. Each request
	// allocates path objects and floods the pathfinder thread plus the main-thread result
	// smoothing. Skip a request when the same entity asked for nearly the same destination
	// (<1.5m) within the last 2s AND still has a usable path to follow. If the path is nearly
	// consumed or the target moved, the request goes through unchanged.
	[HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.FindPath))]
	public static class PathRequestThrottle
	{
		const float SameTargetDistSq = 2.25f; // 1.5m
		const float WindowSeconds = 2f;

		struct Request
		{
			public float time;
			public Vector3 pos;
		}

		static readonly Dictionary<int, Request> lastRequests = new Dictionary<int, Request>();

		public static bool Prefix(EntityAlive __instance, Vector3 targetPos)
		{
			float now = Time.time;
			if (lastRequests.TryGetValue(__instance.entityId, out Request last)
				&& now - last.time < WindowSeconds
				&& (targetPos - last.pos).sqrMagnitude < SameTargetDistSq)
			{
				PathEntity path = __instance.navigator?.getPath();
				if (path != null && path.NodeCountRemaining() > 2)
				{
					return false; // keep following the current path
				}
			}
			if (lastRequests.Count > 512)
			{
				lastRequests.Clear();
			}
			lastRequests[__instance.entityId] = new Request { time = now, pos = targetPos };
			return true;
		}
	}
}

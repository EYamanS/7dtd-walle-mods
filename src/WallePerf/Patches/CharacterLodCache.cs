using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace WallePerf.Patches
{
	// T1.8e: CharacterShaderLODControl.Update calls Camera.main twice and rewrites
	// material.shader.maximumLOD for every material every frame, per character, even when
	// the value is unchanged. Cache the camera once per frame and only write on change.
	[HarmonyPatch(typeof(CharacterShaderLODControl), "Update")]
	public static class CharacterLodCache
	{
		static readonly ConditionalWeakTable<CharacterShaderLODControl, int[]> lastLod = new ConditionalWeakTable<CharacterShaderLODControl, int[]>();

		static Camera cachedCamera;
		static int cameraFrame = -1;

		public static bool Prefix(CharacterShaderLODControl __instance)
		{
			int frame = Time.frameCount;
			if (frame != cameraFrame)
			{
				cachedCamera = Camera.main;
				cameraFrame = frame;
			}
			Camera cam = cachedCamera;
			if (cam == null)
			{
				return false;
			}
			int lod = (Vector3.Distance(cam.transform.position, __instance.transform.position) <= __instance.transitionDistance) ? 200 : 100;
			int[] box = lastLod.GetValue(__instance, _ => new[] { -1 });
			if (box[0] == lod)
			{
				return false;
			}
			box[0] = lod;
			var materials = __instance.materials;
			if (materials != null)
			{
				for (int i = 0; i < materials.Count; i++)
				{
					materials[i].shader.maximumLOD = lod;
				}
			}
			return false;
		}
	}
}

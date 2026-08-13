using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace WallePerf.Patches
{
	// T2.8: EntityPlayerLocal.Update calls cameraTransform.GetComponent<Camera>() every frame
	// (while holding a ranged weapon) although the exact same camera is already cached in the
	// playerCamera field. The transpiler rewrites the field load + GetComponent pair into a
	// direct playerCamera load.
	[HarmonyPatch(typeof(EntityPlayerLocal), "Update")]
	public static class CrosshairCameraFix
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			FieldInfo cameraTransform = AccessTools.Field(typeof(Entity), "cameraTransform");
			FieldInfo playerCamera = AccessTools.Field(typeof(EntityPlayerLocal), "playerCamera");
			CodeInstruction previous = null;
			foreach (CodeInstruction ins in instructions)
			{
				bool isGetComponentCamera = (ins.opcode == OpCodes.Call || ins.opcode == OpCodes.Callvirt)
					&& ins.operand is MethodInfo m
					&& m.Name == "GetComponent"
					&& m.IsGenericMethod
					&& m.GetGenericArguments()[0] == typeof(Camera);
				if (isGetComponentCamera && previous != null && previous.opcode == OpCodes.Ldfld && (object)previous.operand == cameraTransform)
				{
					// [ldfld cameraTransform, callvirt GetComponent<Camera>] -> [ldfld playerCamera]
					yield return new CodeInstruction(OpCodes.Ldfld, playerCamera).WithLabels(previous.labels).WithBlocks(previous.blocks);
					previous = null;
					continue;
				}
				if (previous != null)
				{
					yield return previous;
				}
				previous = ins;
			}
			if (previous != null)
			{
				yield return previous;
			}
		}
	}
}

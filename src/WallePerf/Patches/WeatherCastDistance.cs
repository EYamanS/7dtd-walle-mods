using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace WallePerf.Patches
{
	// T1.2: ParticlesFrameUpdate fires a radius-9m SphereCast with max distance
	// float.PositiveInfinity every frame to find the ground under the camera.
	// The cast origin is camera + 250m, so 700m always reaches the ground; an
	// unbounded fat spherecast is dramatically more expensive in the broadphase.
	[HarmonyPatch(typeof(WeatherManager), "ParticlesFrameUpdate")]
	public static class WeatherCastDistance
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			foreach (CodeInstruction ins in instructions)
			{
				if (ins.opcode == OpCodes.Ldc_R4 && ins.operand is float f && float.IsPositiveInfinity(f))
				{
					yield return new CodeInstruction(OpCodes.Ldc_R4, 700f);
				}
				else
				{
					yield return ins;
				}
			}
		}
	}
}

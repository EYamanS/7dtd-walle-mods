using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace WallePerf.Patches
{
	// T1.8d: Entity.Update allocates a new List<StopAnimatorAudioType> every frame for every
	// entity with monitored animator audio (i.e. any vocalizing zombie) — GC pressure that
	// scales with horde size. Swap the allocation for a reused scratch list via transpiler,
	// leaving the rest of the method untouched. Main-thread only, so one static list is safe.
	[HarmonyPatch(typeof(Entity), "Update")]
	public static class AnimatorAudioScratch
	{
		static readonly List<Entity.StopAnimatorAudioType> scratch = new List<Entity.StopAnimatorAudioType>();

		public static List<Entity.StopAnimatorAudioType> GetScratch()
		{
			scratch.Clear();
			return scratch;
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var getScratch = typeof(AnimatorAudioScratch).GetMethod(nameof(GetScratch));
			foreach (CodeInstruction ins in instructions)
			{
				if (ins.opcode == OpCodes.Newobj && ins.operand is System.Reflection.ConstructorInfo ctor && ctor.DeclaringType == typeof(List<Entity.StopAnimatorAudioType>))
				{
					yield return new CodeInstruction(OpCodes.Call, getScratch);
				}
				else
				{
					yield return ins;
				}
			}
		}
	}
}

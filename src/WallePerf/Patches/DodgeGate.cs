using HarmonyLib;

namespace WallePerf.Patches
{
	// T2.10: The dodge AI task polls at 10Hz with a chunk-entity scan, looking for an
	// attacker whose animation warrants dodging — for every entity with the task, forever,
	// even with no combat anywhere near. Only allow the scan when the entity actually has an
	// attack target; the cooldown branch of the original still runs so timers stay correct.
	[HarmonyPatch(typeof(EAIDodge), nameof(EAIDodge.CanExecute))]
	public static class DodgeGate
	{
		public static bool Prefix(EAIDodge __instance, ref bool __result)
		{
			if (__instance.cooldown <= 0f && __instance.theEntity.GetAttackTarget() == null)
			{
				__result = false;
				return false;
			}
			return true;
		}
	}
}

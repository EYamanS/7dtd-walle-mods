using HarmonyLib;

namespace WallePerf.Patches
{
	// T1.7: PreValidateJobData scans every index of every uploaded mesh on the main thread
	// (up to ~131k iterations per mesh) purely to log an error that never fires on vanilla
	// data. Report success without scanning.
	[HarmonyPatch(typeof(MeshDataManager), "PreValidateJobData")]
	public static class MeshValidateSkip
	{
		public static bool Prefix(ref bool __result)
		{
			__result = true;
			return false;
		}
	}
}

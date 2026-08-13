using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace WallePerf.Patches
{
	public static class TerrainMeshTrim
	{
		// Replaces a "callvirt Mesh.X(...)" (void return) with pops, keeping the IL stack
		// balanced no matter how the arguments were loaded.
		public static IEnumerable<CodeInstruction> StripMeshCall(IEnumerable<CodeInstruction> instructions, string methodName)
		{
			foreach (CodeInstruction ins in instructions)
			{
				if ((ins.opcode == OpCodes.Callvirt || ins.opcode == OpCodes.Call)
					&& ins.operand is MethodInfo m
					&& m.DeclaringType == typeof(Mesh)
					&& m.Name == methodName)
				{
					int pops = m.GetParameters().Length + 1; // args + instance
					for (int i = 0; i < pops; i++)
					{
						yield return new CodeInstruction(OpCodes.Pop);
					}
				}
				else
				{
					yield return ins;
				}
			}
		}
	}

	// T2.4a: Terrain meshes (the game's largest, up to ~786k verts) upload synchronously on
	// the main thread including RecalculateUVDistributionMetrics — a full-mesh pass that only
	// feeds Unity's texture-mip streaming heuristics, which the terrain materials don't use.
	[HarmonyPatch(typeof(VoxelMeshTerrain), nameof(VoxelMeshTerrain.CopyToMesh),
		typeof(MeshFilter), typeof(MeshRenderer), typeof(int), typeof(System.Action))]
	public static class TerrainUvMetricsSkip
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			return TerrainMeshTrim.StripMeshCall(instructions, "RecalculateUVDistributionMetrics");
		}
	}

	// T2.4b (EXPERIMENTAL, default off): also skip RecalculateTangents — another full-mesh
	// main-thread pass. The terrain shader may use tangents for normal mapping; if terrain
	// lighting looks flat/wrong with this on, keep it disabled. Toggle and eyeball a cliff.
	[HarmonyPatch(typeof(VoxelMeshTerrain), nameof(VoxelMeshTerrain.CopyToMesh),
		typeof(MeshFilter), typeof(MeshRenderer), typeof(int), typeof(System.Action))]
	public static class TerrainTangentSkip
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			return TerrainMeshTrim.StripMeshCall(instructions, "RecalculateTangents");
		}
	}
}

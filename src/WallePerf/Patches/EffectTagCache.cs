using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace WallePerf.Patches
{
	// T2.7: EffectManager.GetValue — the game's central stat query, called constantly by AI,
	// UI and movement — re-parses the passive effect's name into a FastTags value INSIDE its
	// loop over installed item mods, on every single call. The result only depends on the
	// enum value, so serve it from a cache: the transpiler replaces
	//     FastTags<TagGroup.Global>.Parse(_passiveEffect.ToStringCached())
	// with a dictionary lookup.
	[HarmonyPatch(typeof(EffectManager), nameof(EffectManager.GetValue))]
	public static class EffectTagCache
	{
		static readonly Dictionary<int, FastTags<TagGroup.Global>> cache = new Dictionary<int, FastTags<TagGroup.Global>>();

		public static FastTags<TagGroup.Global> TagsFor(PassiveEffects _effect)
		{
			if (!cache.TryGetValue((int)_effect, out FastTags<TagGroup.Global> tags))
			{
				tags = FastTags<TagGroup.Global>.Parse(_effect.ToStringCached());
				cache[(int)_effect] = tags;
			}
			return tags;
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo tagsFor = typeof(EffectTagCache).GetMethod(nameof(TagsFor));
			CodeInstruction previous = null;
			foreach (CodeInstruction ins in instructions)
			{
				bool isParse = (ins.opcode == OpCodes.Call || ins.opcode == OpCodes.Callvirt)
					&& ins.operand is MethodInfo parse
					&& parse.Name == "Parse"
					&& parse.DeclaringType == typeof(FastTags<TagGroup.Global>);
				bool prevIsToStringCached = previous != null
					&& (previous.opcode == OpCodes.Call || previous.opcode == OpCodes.Callvirt)
					&& previous.operand is MethodInfo tsc
					&& tsc.Name == "ToStringCached";
				if (isParse && prevIsToStringCached)
				{
					// stack before ToStringCached held the PassiveEffects value:
					// replace [ToStringCached, Parse] with [TagsFor]
					previous = null;
					yield return new CodeInstruction(OpCodes.Call, tagsFor);
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

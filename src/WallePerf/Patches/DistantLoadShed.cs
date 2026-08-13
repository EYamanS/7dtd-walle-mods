using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace WallePerf.Patches
{
	// Tier 3a: the profiler showed ~83% of horde frame time is engine-side (animation,
	// shadow-casting draw calls, physics). These two patches use the game's own
	// closest-player distance ranking (refreshed by EntityActivityUpdate) to shed engine
	// load for far-away enemies, with hysteresis so nothing flickers at the boundary.

	// Distant zombies stop casting shadows (>25m from the nearest player; restored <20m).
	// Every zombie is normally drawn at least twice (view + shadow map) — at range the
	// shadow is barely a smudge, but the draw calls are full price.
	[HarmonyPatch(typeof(World), nameof(World.EntityActivityUpdate))]
	public static class ZombieShadowCull
	{
		const float OffDistSq = 625f; // 25m
		const float OnDistSq = 400f;  // 20m

		class State
		{
			public Renderer[] renderers;
			public ShadowCastingMode[] original;
			public bool off;
		}

		static readonly ConditionalWeakTable<EntityAlive, State> states = new ConditionalWeakTable<EntityAlive, State>();
		static int tick;

		public static void Postfix(World __instance)
		{
			if (++tick % 10 != 0) // every ~0.5s
			{
				return;
			}
			var alives = __instance.EntityAlives;
			for (int i = 0; i < alives.Count; i++)
			{
				EntityAlive entity = alives[i];
				if (!(entity is EntityEnemy))
				{
					continue;
				}
				State state = states.GetValue(entity, _ => new State());
				if (entity.IsDead())
				{
					if (state.off)
					{
						Apply(state, restore: true);
						state.off = false;
					}
					continue;
				}
				float distSq = entity.aiClosestPlayerDistSq;
				if (!state.off && distSq > OffDistSq)
				{
					if (EnsureRenderers(entity, state))
					{
						Apply(state, restore: false);
						state.off = true;
					}
				}
				else if (state.off && distSq < OnDistSq)
				{
					Apply(state, restore: true);
					state.off = false;
				}
			}
		}

		static bool EnsureRenderers(EntityAlive entity, State state)
		{
			if (state.renderers != null)
			{
				for (int i = 0; i < state.renderers.Length; i++)
				{
					if (state.renderers[i] == null)
					{
						state.renderers = null; // model changed, re-fetch
						break;
					}
				}
			}
			if (state.renderers == null)
			{
				Transform model = entity.emodel?.GetModelTransform();
				if (model == null)
				{
					return false;
				}
				state.renderers = model.GetComponentsInChildren<Renderer>(true);
				state.original = new ShadowCastingMode[state.renderers.Length];
				for (int i = 0; i < state.renderers.Length; i++)
				{
					state.original[i] = state.renderers[i].shadowCastingMode;
				}
			}
			return state.renderers.Length > 0;
		}

		static void Apply(State state, bool restore)
		{
			if (state.renderers == null)
			{
				return;
			}
			for (int i = 0; i < state.renderers.Length; i++)
			{
				Renderer renderer = state.renderers[i];
				if (renderer != null)
				{
					renderer.shadowCastingMode = restore ? state.original[i] : ShadowCastingMode.Off;
				}
			}
		}
	}

	// Distant zombies switch their Animator to CullUpdateTransforms: when their renderers
	// are off-screen, Unity skips pose/IK/transform writes entirely (the state machine keeps
	// running, so behavior and timers stay correct). Vanilla forces AlwaysAnimate on every
	// enemy (AvatarController), paying full animation cost for zombies nobody can see.
	// Distance-gated (>25m) so melee-range enemies behind you always animate fully — their
	// bone-driven hit colliders must stay live.
	[HarmonyPatch(typeof(World), nameof(World.EntityActivityUpdate))]
	public static class ZombieAnimatorCull
	{
		const float OffDistSq = 625f; // 25m
		const float OnDistSq = 400f;  // 20m

		class State
		{
			public bool culled;
		}

		static readonly ConditionalWeakTable<EntityAlive, State> states = new ConditionalWeakTable<EntityAlive, State>();
		static int tick;

		public static void Postfix(World __instance)
		{
			if (++tick % 10 != 0)
			{
				return;
			}
			var alives = __instance.EntityAlives;
			for (int i = 0; i < alives.Count; i++)
			{
				EntityAlive entity = alives[i];
				if (!(entity is EntityEnemy) || entity.IsDead())
				{
					continue;
				}
				Animator animator = entity.emodel?.avatarController?.GetAnimator();
				if (animator == null)
				{
					continue;
				}
				State state = states.GetValue(entity, _ => new State());
				float distSq = entity.aiClosestPlayerDistSq;
				if (!state.culled && distSq > OffDistSq)
				{
					animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
					state.culled = true;
				}
				else if (state.culled && distSq < OnDistSq)
				{
					animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
					state.culled = false;
				}
			}
		}
	}
}

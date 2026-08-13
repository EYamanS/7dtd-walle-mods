using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WallePerf.Patches
{
	// T1.8b: ObjectiveRallyPoint.SetupFlags allocates a fresh HashSet every frame (even with
	// zero rally objectives) and does chunk lookups + GetComponent per visible rally point
	// per frame. Reimplemented with an early-out and a reused scratch set.
	[HarmonyPatch(typeof(ObjectiveRallyPoint), nameof(ObjectiveRallyPoint.SetupFlags))]
	public static class RallyFlagsScratch
	{
		static readonly HashSet<ObjectiveRallyPointData> scratch = new HashSet<ObjectiveRallyPointData>();

		public static bool Prefix(List<BaseObjective> objectives)
		{
			bool any = false;
			for (int i = 0; i < objectives.Count; i++)
			{
				if (objectives[i] is ObjectiveRallyPoint rp && rp.isRallyVisible)
				{
					any = true;
					break;
				}
			}
			if (!any)
			{
				return false;
			}

			scratch.Clear();
			foreach (BaseObjective objective in objectives)
			{
				if (!(objective is ObjectiveRallyPoint rallyPoint) || !rallyPoint.isRallyVisible)
				{
					continue;
				}
				Transform blockTransform = ObjectiveRallyPoint.getBlockTransform(rallyPoint.rallyPos);
				if (blockTransform == null)
				{
					continue;
				}
				ObjectiveRallyPointData component = blockTransform.gameObject.GetComponent<ObjectiveRallyPointData>();
				if (component == null)
				{
					continue;
				}
				Quest ownerQuest = rallyPoint.OwnerQuest;
				if (ownerQuest.CurrentPhase == rallyPoint.Phase)
				{
					if (scratch.Add(component))
					{
						component.ClearAllFlags();
					}
					component.AddFlag(rallyPoint.rallyMarkerType, ownerQuest.SharedOwnerID == -1);
				}
			}
			foreach (ObjectiveRallyPointData data in scratch)
			{
				data.UpdateAllFlags();
			}
			scratch.Clear();
			return false;
		}
	}
}

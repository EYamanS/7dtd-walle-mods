using System;
using System.Collections.Generic;
using Audio;
using HarmonyLib;
using Platform;
using UnityEngine;

namespace WalleQoL.Patches
{
	public static class QuickDeposit
	{
		public const string CommandName = "walle_deposit";
		public const string CommandSuffix = ":" + CommandName;

		// The actual deposit: top up existing stacks in the container from the backpack,
		// never creating new stacks — the same semantic as the single-arrow button
		// (XUiM_LootContainer.StashItems with EItemMoveKind.FillOnly), done on data instead
		// of UI so the container never has to open. Respects player-locked backpack slots.
		public static bool Run(TEFeatureStorage storage, EntityPlayerLocal player)
		{
			if (player == null || player.bag == null)
			{
				return false;
			}
			if ((storage.lockFeature != null && storage.lockFeature.IsLocked() && !storage.lockFeature.IsUserAllowed(PlatformManager.InternalLocalUserIdentifier)) || storage.isJammed)
			{
				Manager.BroadcastPlayByLocalPlayer(storage.ToWorldPos().ToVector3() + Vector3.one * 0.5f, "Misc/locked");
				return false;
			}

			Bag bag = player.bag;
			ItemStack[] slots = bag.GetSlots();
			PackedBoolArray lockedSlots = bag.LockedSlots;
			TileEntityComposite parent = storage.Parent;

			bool anyMoved = false;
			// itemValue.type -> (sample value for icon/name, total count moved)
			Dictionary<int, (ItemValue value, int moved)> movedByType = new Dictionary<int, (ItemValue, int)>();
			// Batch the whole deposit into a single SetModified/net package instead of one per merged stack.
			parent.SetDisableModifiedCheck(true);
			try
			{
				for (int i = 0; i < slots.Length; i++)
				{
					if (lockedSlots != null && i < lockedSlots.Length && lockedSlots[i])
					{
						continue;
					}
					ItemStack stack = slots[i];
					if (stack == null || stack.IsEmpty())
					{
						continue;
					}
					int before = stack.count;
					var result = storage.TryStackItem(0, stack);
					if (result.anyMoved)
					{
						anyMoved = true;
						int moved = before - stack.count;
						int type = stack.itemValue.type;
						movedByType[type] = movedByType.TryGetValue(type, out var prev)
							? (prev.value, prev.moved + moved)
							: (stack.itemValue.Clone(), moved);
						if (stack.count == 0)
						{
							slots[i] = ItemStack.Empty;
						}
					}
				}
			}
			finally
			{
				parent.SetDisableModifiedCheck(false);
			}

			if (anyMoved)
			{
				bag.onBackpackChanged();
				storage.SetModified();
				// Same right-side item feed as pickups, but as "-N" entries per item type.
				XUiC_CollectedItemList feed = player.PlayerUI?.xui?.CollectedItemList;
				if (feed != null)
				{
					foreach (var kv in movedByType)
					{
						feed.RemoveItemStack(new ItemStack(kv.Value.value, kv.Value.moved));
					}
				}
			}
			else
			{
				GameManager.ShowTooltip(player, Localization.Get("walleDepositNone"));
			}
			return true;
		}
	}

	// QuickDeposit 1/3: register the radial command on every storage feature. The command
	// array is cached per block type (BlockCompositeTileEntity.commands), so registration
	// must be unconditional; per-instance gating happens in DepositEnablePatch below.
	[HarmonyPatch(typeof(TEFeatureStorage), nameof(TEFeatureStorage.InitBlockActivationCommands))]
	public static class DepositCommandPatch
	{
		public static void Postfix(TEFeatureStorage __instance, Action<BlockActivationCommand, TileEntityComposite.EBlockCommandOrder, TileEntityFeatureData> _addCallback)
		{
			_addCallback(new BlockActivationCommand(QuickDeposit.CommandName, "store_all_up", _enabled: true), TileEntityComposite.EBlockCommandOrder.Normal, __instance.FeatureData);
		}
	}

	// QuickDeposit 2/3: enable the command only on player-placed storage the player may
	// actually access (not on world loot, locked-out chests, or jammed quest containers).
	[HarmonyPatch(typeof(TileEntityComposite), nameof(TileEntityComposite.UpdateBlockActivationCommands))]
	public static class DepositEnablePatch
	{
		public static void Postfix(TileEntityComposite __instance, BlockActivationCommand[] _commands)
		{
			for (int i = 0; i < _commands.Length; i++)
			{
				if (_commands[i].text == null || !_commands[i].text.EndsWith(QuickDeposit.CommandSuffix, StringComparison.Ordinal))
				{
					continue;
				}
				TEFeatureStorage storage = __instance.GetFeature<TEFeatureStorage>();
				_commands[i].enabled = storage != null && storage.bPlayerStorage && !storage.isJammed
					&& (storage.lockFeature == null || !storage.lockFeature.IsLocked() || storage.lockFeature.IsUserAllowed(PlatformManager.InternalLocalUserIdentifier));
			}
		}
	}

	// QuickDeposit 3/3: handle activation. Patched on the composite's string-typed overload
	// (commands arrive as "featurename:walle_deposit") to avoid span-typed patch parameters.
	[HarmonyPatch(typeof(TileEntityComposite), nameof(TileEntityComposite.OnBlockActivated),
		typeof(BlockActivationCommand[]), typeof(string), typeof(WorldBase), typeof(Vector3i), typeof(BlockValue), typeof(EntityPlayerLocal))]
	public static class DepositActivatePatch
	{
		public static void Postfix(TileEntityComposite __instance, string _commandName, EntityPlayerLocal _player, ref bool __result)
		{
			if (_commandName == null || !_commandName.EndsWith(QuickDeposit.CommandSuffix, StringComparison.Ordinal))
			{
				return;
			}
			TEFeatureStorage storage = __instance.GetFeature<TEFeatureStorage>();
			if (storage != null)
			{
				__result = QuickDeposit.Run(storage, _player);
			}
		}
	}
}

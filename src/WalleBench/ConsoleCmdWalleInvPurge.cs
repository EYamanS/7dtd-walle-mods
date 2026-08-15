using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine.Scripting;

namespace WalleBench
{
	// Console command "walleinvpurge <playerDataDir> <itemName>": removes every stack of the
	// named item from bag + toolbelt of every .ttp player file in the directory and saves
	// the file back (the game's Save() keeps a .bak of the previous state). Run only while
	// the affected players are offline, and against a copy first.
	[Preserve]
	public class ConsoleCmdWalleInvPurge : ConsoleCmdAbstract
	{
		public override string[] getCommands()
		{
			return new string[1] { "walleinvpurge" };
		}

		public override string getHelp()
		{
			return "walleinvpurge <playerDataDir> <itemName> - remove all stacks of the item from every .ttp player file in the directory and save";
		}

		public override string getDescription()
		{
			return "purge an item from saved player inventories";
		}

		public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
		{
			SdtdConsole console = SingletonMonoBehaviour<SdtdConsole>.Instance;
			if (_params.Count < 2)
			{
				console.Output(getHelp());
				return;
			}
			string itemName = _params[_params.Count - 1];
			string dir = string.Join(" ", _params.GetRange(0, _params.Count - 1)).TrimEnd('/', '\\');
			if (!Directory.Exists(dir))
			{
				console.Output("[WallePurge] directory not found: " + dir);
				return;
			}
			foreach (string file in Directory.GetFiles(dir, "*.ttp"))
			{
				string playerId = Path.GetFileNameWithoutExtension(file);
				PlayerDataFile pdf = new PlayerDataFile();
				pdf.Load(dir, playerId);
				if (!pdf.bLoaded)
				{
					console.Output($"[WallePurge] {playerId}: FAILED TO LOAD, skipped");
					continue;
				}
				int removed = 0;
				ItemStack[] bagSlots = pdf.bag.GetSlots();
				for (int i = 0; i < bagSlots.Length; i++)
				{
					if (Matches(bagSlots[i], itemName))
					{
						removed += bagSlots[i].count;
						pdf.bag.SetSlot(i, ItemStack.Empty, callChangedEvent: false);
					}
				}
				for (int i = 0; i < pdf.inventory.Length; i++)
				{
					if (Matches(pdf.inventory[i], itemName))
					{
						removed += pdf.inventory[i].count;
						pdf.inventory[i] = ItemStack.Empty;
					}
				}
				if (removed > 0)
				{
					pdf.bModifiedSinceLastSave = true;
					pdf.Save(dir, playerId);
					console.Output($"[WallePurge] {playerId}: removed {removed} x {itemName}, saved");
				}
				else
				{
					console.Output($"[WallePurge] {playerId}: nothing to remove");
				}
			}
			console.Output("[WallePurge] done");
		}

		static bool Matches(ItemStack stack, string itemName)
		{
			if (stack == null || stack.IsEmpty())
			{
				return false;
			}
			string name = stack.itemValue.ItemClass?.GetItemName();
			return name != null && name.Equals(itemName, StringComparison.OrdinalIgnoreCase);
		}
	}
}

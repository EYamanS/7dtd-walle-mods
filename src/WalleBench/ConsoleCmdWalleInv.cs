using System.Collections.Generic;
using System.IO;
using UnityEngine.Scripting;

namespace WalleBench
{
	// Console command "walleinv <playerDataDir>": dumps bag/toolbelt/equipment of every
	// player .ttp file in the given directory (offline inspection of saved player data).
	// Read-only — never writes player files. Run it against a COPY of the Player folder.
	[Preserve]
	public class ConsoleCmdWalleInv : ConsoleCmdAbstract
	{
		static readonly FastTags<TagGroup.Global> questTag = FastTags<TagGroup.Global>.Parse("quest");

		public override string[] getCommands()
		{
			return new string[1] { "walleinv" };
		}

		public override string getHelp()
		{
			return "walleinv <playerDataDir> - list bag/toolbelt/equipment of every .ttp player file in the directory (read-only)";
		}

		public override string getDescription()
		{
			return "inspect saved player inventories from .ttp files";
		}

		public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
		{
			SdtdConsole console = SingletonMonoBehaviour<SdtdConsole>.Instance;
			if (_params.Count < 1)
			{
				console.Output(getHelp());
				return;
			}
			string dir = string.Join(" ", _params).TrimEnd('/', '\\');
			if (!Directory.Exists(dir))
			{
				console.Output("[WalleInv] directory not found: " + dir);
				return;
			}
			string[] files = Directory.GetFiles(dir, "*.ttp");
			console.Output($"[WalleInv] {files.Length} player file(s) in {dir}");
			foreach (string file in files)
			{
				string playerId = Path.GetFileNameWithoutExtension(file);
				PlayerDataFile pdf = new PlayerDataFile();
				pdf.Load(dir, playerId);
				if (!pdf.bLoaded)
				{
					console.Output($"[WalleInv] ==== {playerId}: FAILED TO LOAD ====");
					continue;
				}
				console.Output($"[WalleInv] ==== player {playerId} ====");
				DumpStacks(console, "belt", pdf.inventory);
				DumpStacks(console, "bag ", pdf.bag.GetSlots());
				ItemValue[] equip = pdf.equipment.GetItems();
				for (int i = 0; i < equip.Length; i++)
				{
					if (equip[i] != null && equip[i].type != 0)
					{
						console.Output($"[WalleInv]   equip[{i}]: {Name(equip[i])}");
					}
				}
			}
		}

		static void DumpStacks(SdtdConsole console, string label, ItemStack[] stacks)
		{
			if (stacks == null)
			{
				return;
			}
			for (int i = 0; i < stacks.Length; i++)
			{
				ItemStack stack = stacks[i];
				if (stack == null || stack.IsEmpty())
				{
					continue;
				}
				string flags = "";
				ItemClass itemClass = stack.itemValue.ItemClass;
				if (itemClass != null && itemClass.HasAnyTags(questTag))
				{
					flags += " [QUEST]";
				}
				console.Output($"[WalleInv]   {label}[{i,2}]: {stack.count,5} x {Name(stack.itemValue)}{flags}");
			}
		}

		static string Name(ItemValue itemValue)
		{
			ItemClass itemClass = itemValue.ItemClass;
			if (itemClass == null)
			{
				return "unknown(type " + itemValue.type + ")";
			}
			return itemClass.GetItemName();
		}
	}
}

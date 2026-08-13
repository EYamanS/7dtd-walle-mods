using System.Collections.Generic;
using UnityEngine.Scripting;

namespace WallePerf
{
	// Console command "walleperf": toggle all performance patches at runtime, so A/B
	// benchmarks (see WalleBench's "bench") don't need a game restart.
	[Preserve]
	public class ConsoleCmdWallePerf : ConsoleCmdAbstract
	{
		public override string[] getCommands()
		{
			return new string[1] { "walleperf" };
		}

		public override string getHelp()
		{
			return "walleperf on|off|status - enable/disable all WallePerf patches at runtime (for A/B benchmarking)";
		}

		public override string getDescription()
		{
			return "toggles WallePerf performance patches at runtime";
		}

		public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
		{
			SdtdConsole console = SingletonMonoBehaviour<SdtdConsole>.Instance;
			string sub = _params.Count > 0 ? _params[0].ToLowerInvariant() : "status";
			switch (sub)
			{
				case "off":
					if (!WallePerfMod.PatchesActive)
					{
						console.Output("[WallePerf] patches already OFF");
						return;
					}
					WallePerfMod.RemovePatches();
					console.Output("[WallePerf] patches OFF — vanilla behavior restored");
					break;
				case "on":
					if (WallePerfMod.PatchesActive)
					{
						console.Output("[WallePerf] patches already ON");
						return;
					}
					WallePerfMod.ApplyPatches();
					console.Output("[WallePerf] patches ON");
					break;
				default:
					console.Output("[WallePerf] patches are " + (WallePerfMod.PatchesActive ? "ON" : "OFF") + " (use 'walleperf on' / 'walleperf off')");
					break;
			}
		}
	}
}

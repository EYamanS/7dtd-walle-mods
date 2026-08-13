using HarmonyLib;

namespace WallePerf.Patches
{
	// T2.6b: Every HUD stat bar calls RefreshBindings() every frame WHILE THE HUD IS HIDDEN
	// (screenshot mode, cinematic, partial-hide). Refresh once on the transition to hidden,
	// then skip entirely until it comes back; the vanilla hudWasHidden flag already forces a
	// full refresh (IsDirty) on unhide.
	[HarmonyPatch(typeof(XUiC_HUDStatBar), nameof(XUiC_HUDStatBar.Update))]
	public static class StatBarHiddenSkip
	{
		public static bool Prefix(XUiC_HUDStatBar __instance)
		{
			GUIWindowManager windowManager = __instance.xui.playerUI.windowManager;
			if (windowManager.IsFullHUDDisabled() || (!__instance.xui.DragAndDropWindow.InMenu && windowManager.IsHUDPartialHidden()))
			{
				if (!__instance.hudWasHidden)
				{
					__instance.hudWasHidden = true;
					__instance.RefreshBindings();
				}
				return false;
			}
			return true;
		}
	}
}

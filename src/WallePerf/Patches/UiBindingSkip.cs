using System;
using HarmonyLib;

namespace WallePerf.Patches
{
	// T1.3: BindingInfo.RefreshValue detects whether any bound value changed, but only uses
	// that flag to skip rebuilding the result string — the expensive parse-and-assign block
	// runs unconditionally for every binding of every refreshing UI controller, every frame.
	// This replacement keeps vanilla behavior but skips the parse when nothing changed.
	// Safety: still re-parses when the cached result contains dynamic "{cvar(" text, because
	// that is substituted at parse time (BindingsManager.ReplaceCVars) and can change without
	// any binding value changing.
	[HarmonyPatch(typeof(BindingInfo), nameof(BindingInfo.RefreshValue))]
	public static class UiBindingSkip
	{
		public static bool Prefix(BindingInfo __instance)
		{
			BindingInfo b = __instance;
			bool changed = b.cachedResultValue == null;
			for (int i = 0; i < b.bindingList.Count; i++)
			{
				string value = b.bindingList[i].GetValue() ?? "";
				if (i < b.cachedBindingValues.Count)
				{
					changed |= !string.Equals(b.cachedBindingValues[i], value, StringComparison.Ordinal);
					b.cachedBindingValues[i] = value;
				}
				else
				{
					changed = true;
					b.cachedBindingValues.Add(value);
				}
			}

			if (changed)
			{
				string result = b.sourceText;
				if (b.bindingList.Count == 1 && result.Equals(b.bindingList[0].SourceText, StringComparison.Ordinal))
				{
					result = b.cachedBindingValues[0] ?? "";
				}
				else
				{
					for (int j = 0; j < b.bindingList.Count; j++)
					{
						BindingItem item = b.bindingList[j];
						result = result.Replace(item.SourceText, b.cachedBindingValues[j]);
					}
				}
				b.cachedResultValue = result;
			}

			string text = b.cachedResultValue;
			bool hasDynamicCvar = text.Contains("{cvar(");

			// The skip: unchanged value, parsers already known, no parse-time cvar substitution.
			if (!changed && b.parsersDetected && !hasDynamicCvar)
			{
				return false;
			}

			if (hasDynamicCvar)
			{
				text = BindingsManager.ReplaceCVars(text);
			}
			try
			{
				if (!b.parsersDetected)
				{
					if (ParsingMethodCache.Instance.TryGetParsingDelegate(b.View, b.attributeName, out var parsingDelegate) && parsingDelegate.TryGetDelegateForSourceType(typeof(string), out b.parsingDelegateView))
					{
						b.parsingDelegateView(b.View, text);
					}
					if (b.parsingDelegateView == null && b.View.Controller != null && ParsingMethodCache.Instance.TryGetParsingDelegate(b.View.Controller, b.attributeName, out parsingDelegate) && parsingDelegate.TryGetDelegateForSourceType(typeof(string), out b.parsingDelegateController))
					{
						b.parsingDelegateController(b.View.Controller, text);
					}
					if (b.parsingDelegateView == null && b.parsingDelegateController == null)
					{
						b.View.ParseAttributeViewAndController(b.attributeName, text);
					}
					b.parsersDetected = true;
				}
				else if (b.parsingDelegateView != null)
				{
					b.parsingDelegateView(b.View, text);
				}
				else if (b.parsingDelegateController != null)
				{
					b.parsingDelegateController(b.View.Controller, text);
				}
				else
				{
					b.View.ParseAttributeViewAndController(b.attributeName, text);
				}
			}
			catch (Exception e)
			{
				Log.Error("[XUi] Exception parsing result of binding. Attribute: '" + b.attributeName + "', binding string: '" + b.sourceText + "', binding result: '" + text + "', view hierarchy: " + b.View.GetXuiHierarchy() + ":");
				Log.Exception(e);
			}
			return false;
		}
	}
}

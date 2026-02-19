// ────────────────────────────────────────────────
//  PlayerListPatch.cs
//  Injects a Block button into each WhoListDataEntry
//  using exact class names from the ATLYSS decomp.
// ────────────────────────────────────────────────
using HarmonyLib;
using System.Collections;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PModeration
{
    internal static class PlayerListPatchHelper
    {
        public static void InjectAllButtons(WhoMenuCell whoMenu)
        {
            if (whoMenu == null) return;

            // _cell_ScrollViewContent is on WhoMenuCell (confirmed in decomp)
            var contentField = typeof(WhoMenuCell).GetField(
                "_cell_ScrollViewContent",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Transform content = contentField?.GetValue(whoMenu) as Transform;

            if (content == null)
            {
                if (Plugin.CfgDebugMode.Value)
                    Plugin.Log.LogWarning("PlayerListPatch: _cell_ScrollViewContent not found — falling back to FindObjectsOfType.");

                foreach (var e in Object.FindObjectsOfType<WhoListDataEntry>())
                    TryInject(e);
                return;
            }

            int injected = 0;
            foreach (Transform child in content)
            {
                var entry = child.GetComponent<WhoListDataEntry>();
                if (entry != null)
                {
                    TryInject(entry);
                    injected++;
                }
            }

            if (Plugin.CfgDebugMode.Value)
                Plugin.Log.LogInfo($"PlayerListPatch: Injected block buttons into {injected} entries.");
        }

        private static void TryInject(WhoListDataEntry entry)
        {
            if (entry == null || entry._player == null) return;
            UIBlockHelper.AddOrUpdateBlockButton(entry.gameObject, entry._player);
        }
    }

    // ── Patch 1: Postfix on Process_FindEntries (the coroutine that builds the list) ──
    // We patch it as a normal method postfix — Harmony intercepts the coroutine
    // start, so we wait for _locatingPeers to go false (meaning the coroutine finished)
    // by using our own follow-up coroutine.
    [HarmonyPatch(typeof(WhoMenuCell), "Process_FindEntries")]
    public static class WhoMenuCell_ProcessFindEntriesPatch
    {
        [HarmonyPostfix]
        public static void Postfix(WhoMenuCell __instance, ref IEnumerator __result)
        {
            // Wrap the original coroutine so we inject after it completes
            __result = WrapCoroutine(__instance, __result);
        }

        private static IEnumerator WrapCoroutine(WhoMenuCell whoMenu, IEnumerator original)
        {
            // Run the original coroutine to completion first
            while (original.MoveNext())
                yield return original.Current;

            // Now the list is fully built — inject buttons
            PlayerListPatchHelper.InjectAllButtons(whoMenu);
        }
    }

    // ── Patch 2: Refresh_PlayerList as a safety net ───────────────────────────
    // Catches edge cases where Process_FindEntries isn't called but the list exists
    [HarmonyPatch(typeof(WhoMenuCell), "Refresh_PlayerList")]
    public static class WhoMenuCell_RefreshPatch
    {
        [HarmonyPostfix]
        public static void Postfix(WhoMenuCell __instance)
        {
            // Delay slightly longer than the game's internal 0.65s wait
            Plugin.Instance.StartCoroutine(DelayedInject(__instance));
        }

        private static IEnumerator DelayedInject(WhoMenuCell whoMenu)
        {
            yield return new WaitForSeconds(1.1f);
            PlayerListPatchHelper.InjectAllButtons(whoMenu);
        }
    }
}
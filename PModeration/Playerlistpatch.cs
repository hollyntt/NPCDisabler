using HarmonyLib;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace PModeration
{
    [HarmonyPatch(typeof(WhoMenuCell), "Process_FindEntries")]
    public static class WhoMenuCell_ProcessFindEntriesPatch
    {
        [HarmonyPostfix]
        public static void Postfix(WhoMenuCell __instance, ref IEnumerator __result)
        {
            __result = WrapCoroutine(__instance, __result);
        }

        private static IEnumerator WrapCoroutine(WhoMenuCell whoMenu, IEnumerator original)
        {
            // 1. Wait for the game to finish generating the list (It has a 0.65s delay internally)
            while (original.MoveNext())
                yield return original.Current;

            // 2. Wait one extra frame to ensure Unity creates the GameObjects
            yield return null;

            // 3. Inject buttons
            InjectButtons(whoMenu);
        }

        private static void InjectButtons(WhoMenuCell whoMenu)
        {
            if (whoMenu == null) return;

            // --- REFLECTION FIX ---
            // In the decompiled code, "_scrollRectContent" is defined in WhoMenuCell.
            // "_cell_ScrollViewContent" was likely in the base class, causing the previous error.
            FieldInfo contentField = typeof(WhoMenuCell).GetField("_scrollRectContent", BindingFlags.Instance | BindingFlags.NonPublic);
            
            if (contentField == null)
            {
                // Fallback: try the base class name just in case
                contentField = typeof(WhoMenuCell).GetField("_cell_ScrollViewContent", BindingFlags.Instance | BindingFlags.NonPublic);
            }

            if (contentField == null)
            {
                if (Plugin.CfgDebugMode.Value) Plugin.Log.LogError("PlayerListPatch: Could not find content field (_scrollRectContent).");
                return;
            }

            Transform contentTransform = contentField.GetValue(whoMenu) as Transform;
            // ---------------------

            if (contentTransform == null) return;

            // Iterate ONLY the children of this specific menu
            foreach (Transform child in contentTransform)
            {
                if (!child.gameObject.activeSelf) continue;

                var entry = child.GetComponent<WhoListDataEntry>();
                
                // Skip invalid entries or yourself
                if (entry == null || entry._player == null || entry._player.isLocalPlayer) continue;

                // Inject Logic
                UIBlockHelper.AddOrUpdateBlockButton(entry.gameObject, entry._player);
                UIBlockHelper.AddOrUpdateGlobalNameButton(entry.gameObject, entry._player);
            }

            // Force layout update to prevent overlap
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentTransform as RectTransform);
        }
    }
}
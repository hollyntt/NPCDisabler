using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Nessie.ATLYSS.EasySettings;
using AtlyssCommandLib;
using AtlyssCommandLib.API;
using static AtlyssCommandLib.API.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;
using System.Reflection;

namespace PModeration
{
    [BepInPlugin(ModInfo.GUID, ModInfo.NAME, ModInfo.VERSION)]
    [BepInDependency("Soggy_Pancake.CommandLib")]
    [BepInDependency("Nessie.ATLYSS.EasySettings", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("CodeTalker")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance;
        internal static ManualLogSource Log;
        private readonly Harmony harmony = new Harmony(ModInfo.GUID);

        // --- CONFIG ---
        public static ConfigEntry<bool> CfgEnabled;
        public static ConfigEntry<bool> CfgHardBlock;
        public static ConfigEntry<bool> CfgCensorChat;
        public static ConfigEntry<string> CfgCensorReplacement;
        public static ConfigEntry<bool> CfgDebugMode;

        // --- DATA ---
        public static HashSet<ulong> BlockedSteamIDs = new HashSet<ulong>();
        public static HashSet<ulong> HiddenGlobalNameIDs = new HashSet<ulong>();
        
        private string blockListPath;
        private string globalNameListPath;
        private float visualRefreshTimer = 0f;
        private const string TARGET_GLOBAL_OBJ_NAME = "_text_playerGlobalName";

        void Awake()
        {
            Instance = this;
            Log = Logger;
            blockListPath = Path.Combine(Paths.ConfigPath, "PModeration_BlockList.json");
            globalNameListPath = Path.Combine(Paths.ConfigPath, "PModeration_GlobalNameHides.json");

            InitConfig();
            
            Settings.OnInitialized.AddListener(AddSettings);
            Settings.OnApplySettings.AddListener(() => { Config.Save(); ForceRefreshAll(); });

            LoadBlockList();
            LoadGlobalNameList();
            RegisterModCommands();

            harmony.PatchAll();
            Log.LogInfo($"{ModInfo.NAME} v{ModInfo.VERSION} Loaded.");
        }

        void Update()
        {
            if (!CfgEnabled.Value) return;
            visualRefreshTimer += Time.deltaTime;
            if (visualRefreshTimer >= 1.0f) { visualRefreshTimer = 0f; RefreshBlockedPlayersInScene(); }
        }

        private void ProcessPlayer(Player p)
        {
            if (p == null || p.isLocalPlayer) return;
            if (!ulong.TryParse(p._steamID, out ulong sID)) return;

            bool isBlocked = BlockedSteamIDs.Contains(sID) && CfgEnabled.Value;
            bool isGlobalHidden = HiddenGlobalNameIDs.Contains(sID);

            // 1. Block Logic
            if (CfgHardBlock.Value)
            {
                // Hard Block: Nuke the whole object
                if (isBlocked && p.gameObject.activeSelf) p.gameObject.SetActive(false);
                else if (!isBlocked && !p.gameObject.activeSelf) p.gameObject.SetActive(true);
            }
            else
            {
                // Soft Block: Hide Visuals/Audio
                if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);

                // --- VISUALS FIX: Use forceRenderingOff ---
                // This prevents the "Exploded Character" glitch because we don't mess with .enabled
                foreach (var r in p.GetComponentsInChildren<Renderer>(true))
                {
                    r.forceRenderingOff = isBlocked;
                }

                // UI & Audio still need traditional disabling
                foreach (var c in p.GetComponentsInChildren<Canvas>(true)) 
                {
                    c.enabled = !isBlocked;
                }
                
                foreach (var a in p.GetComponentsInChildren<AudioSource>(true)) 
                {
                    // If/else statement to attempt to fix that player skill audio bug
                    if (isBlocked)
                    {
                        if (a.isPlaying) a.Stop();
                        a.enabled = false;
                    }
                    else
                    {
                        a.enabled = true;
                    }
                }
                
                // Hide non-renderer children that might persist (like particles without renderers)
                // We skip "_" prefix to avoid breaking game systems found on the player root
                foreach (Transform child in p.transform)
                {
                    if (child.name.StartsWith("_")) continue; 
                    child.gameObject.SetActive(!isBlocked);
                }
            }

            // 2. Global Name Logic
            if (!isBlocked)
            {
                Transform globalNameObj = FindChildRecursive(p.transform, TARGET_GLOBAL_OBJ_NAME);
                if (globalNameObj != null)
                {
                    bool shouldBeActive = !isGlobalHidden;
                    if (globalNameObj.gameObject.activeSelf != shouldBeActive)
                    {
                        globalNameObj.gameObject.SetActive(shouldBeActive);
                    }
                }
            }
        }

        private Transform FindChildRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name.Equals(name)) return child;
                Transform found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        public void ForceRefreshAll()
        {
            foreach (var p in FindObjectsOfType<Player>()) { if (p == null || p.isLocalPlayer) continue; ProcessPlayer(p); }
        }

        private void RefreshBlockedPlayersInScene() => ForceRefreshAll();
        public void AddBlock(ulong steamID, string name = null) { if (BlockedSteamIDs.Add(steamID)) { SaveBlockList(); ForceRefreshAll(); PModerationAPI.NotifyBlocked(steamID); } }
        public void RemoveBlock(ulong steamID) { if (BlockedSteamIDs.Remove(steamID)) { SaveBlockList(); ForceRefreshAll(); PModerationAPI.NotifyUnblocked(steamID); } }
        public bool IsBlocked(ulong steamID) => BlockedSteamIDs.Contains(steamID);
        public bool IsGlobalNameHidden(ulong steamID) => HiddenGlobalNameIDs.Contains(steamID);
        public void HideGlobalName(ulong steamID) { if (HiddenGlobalNameIDs.Add(steamID)) { SaveGlobalNameList(); ForceRefreshAll(); } }
        public void UnhideGlobalName(ulong steamID) { if (HiddenGlobalNameIDs.Remove(steamID)) { SaveGlobalNameList(); ForceRefreshAll(); } }

        private void LoadBlockList() { if (!File.Exists(blockListPath)) return; try { var l = JsonConvert.DeserializeObject<List<ulong>>(File.ReadAllText(blockListPath)); if (l != null) BlockedSteamIDs = new HashSet<ulong>(l); } catch { } }
        private void SaveBlockList() { File.WriteAllText(blockListPath, JsonConvert.SerializeObject(BlockedSteamIDs.ToList(), Formatting.Indented)); }
        private void LoadGlobalNameList() { if (!File.Exists(globalNameListPath)) return; try { var l = JsonConvert.DeserializeObject<List<ulong>>(File.ReadAllText(globalNameListPath)); if (l != null) HiddenGlobalNameIDs = new HashSet<ulong>(l); } catch { } }
        private void SaveGlobalNameList() { File.WriteAllText(globalNameListPath, JsonConvert.SerializeObject(HiddenGlobalNameIDs.ToList(), Formatting.Indented)); }

        private void InitConfig()
        {
            CfgEnabled = Config.Bind("1. General", "Enabled", true, "Master switch");
            CfgHardBlock = Config.Bind("2. Blocking", "Hard Block", false, "True = Disables GameObject. False = Hides Visuals.");
            CfgCensorChat = Config.Bind("3. Chat", "Censor Chat", true, "Replace blocked players' chat.");
            CfgCensorReplacement = Config.Bind("3. Chat", "Replacement Text", "[BLOCKED]", "Text to show.");
            CfgDebugMode = Config.Bind("4. Advanced", "Debug Mode", false, "Log details.");
        }

        private void AddSettings()
        {
            var tab = Settings.GetOrAddCustomTab(ModInfo.NAME);
            tab.AddHeader("General"); tab.AddToggle(CfgEnabled);
            tab.AddHeader("Blocking"); tab.AddToggle(CfgHardBlock);
            tab.AddHeader("Chat"); tab.AddToggle(CfgCensorChat); tab.AddTextField(CfgCensorReplacement);
            tab.AddHeader("Advanced"); tab.AddToggle(CfgDebugMode);
        }

        private void RegisterModCommands()
        {
            var client = new CommandOptions { chatCommand = ChatCommandType.ClientSide };

            // Existing commands (block, unblock, blocklist, export, import)
            RegisterCommand("block", "Locally hides a player's visuals (and optionally chat).", (c, a) =>
            {
                if (a.Length == 0) return false;
                string name = string.Join(" ", a);
                var target = FindPlayerByName(name);
                if (target != null && ulong.TryParse(target._steamID, out ulong id))
                {
                    AddBlock(id, target._nickname);
                    NotifyCaller(c, $"<color=red>[Block]</color> Hid {target._nickname}.", Color.white);
                }
                else
                {
                    NotifyCaller(c, $"<color=yellow>[Block]</color> Player '{name}' not found.", Color.white);
                }
                return true;
            }, client);

            RegisterCommand("unblock", "Unhides a player by name or SteamID.", (c, a) =>
            {
                if (a.Length == 0) return false;
                string input = string.Join(" ", a);

                if (ulong.TryParse(input, out ulong id))
                {
                    RemoveBlock(id);
                    ForceRefreshAll();
                    NotifyCaller(c, $"<color=green>[Unblock]</color> Unblocked ID {id}.", Color.white);
                    return true;
                }

                var target = FindPlayerByName(input);
                if (target != null && ulong.TryParse(target._steamID, out ulong sid))
                {
                    RemoveBlock(sid);
                    ForceRefreshAll();
                    NotifyCaller(c, $"<color=green>[Unblock]</color> Unblocked {target._nickname}.", Color.white);
                }
                else
                {
                    NotifyCaller(c, $"<color=yellow>[Unblock]</color> Player not found. Use SteamID if hidden.", Color.white);
                }
                return true;
            }, client);

            RegisterCommand("blocklist", "Lists all blocked SteamIDs.", (c, _) =>
            {
                NotifyCaller(c, $"<color=orange>Blocked Users ({BlockedSteamIDs.Count}):</color>", Color.white);
                foreach (var id in BlockedSteamIDs) NotifyCaller(c, $"- {id}", Color.white);
                return true;
            }, client);

            RegisterCommand("blockexport", "Exports blocklist to a file.", (c, a) =>
            {
                string path = a.Length > 0 ? a[0] : Path.Combine(Paths.ConfigPath, "PModeration_BlockList_Export.json");
                File.WriteAllText(path, JsonConvert.SerializeObject(BlockedSteamIDs, Formatting.Indented));
                NotifyCaller(c, $"<color=green>[Export]</color> Blocklist saved to {path}", Color.white);
                return true;
            }, client);

            RegisterCommand("blockimport", "Imports blocklist from a file.", (c, a) =>
            {
                if (a.Length == 0) return false;
                string path = a[0];
                if (!File.Exists(path))
                {
                    NotifyCaller(c, $"<color=red>[Import]</color> File not found: {path}", Color.white);
                    return true;
                }
                var json = File.ReadAllText(path);
                var list = JsonConvert.DeserializeObject<List<ulong>>(json);
                BlockedSteamIDs.UnionWith(list);
                SaveBlockList();
                ForceRefreshAll();
                NotifyCaller(c, $"<color=green>[Import]</color> Imported {list.Count} IDs.", Color.white);
                return true;
            }, client);

            RegisterCommand("hideglobal", "Hides only a player's @global name (does not hide model or RP title).", (c, a) =>
    {
        if (a.Length == 0) return false;
        string name = string.Join(" ", a);
        var target = FindPlayerByName(name);
        if (target != null && ulong.TryParse(target._steamID, out ulong id))
        {
            HideGlobalName(id);
            NotifyCaller(c, $"<color=cyan>[HideGlobal]</color> Hid @global name for {target._nickname}.", Color.white);
        }
        else
        {
            NotifyCaller(c, $"<color=yellow>[HideGlobal]</color> Player '{name}' not found.", Color.white);
        }
        return true;
    }, client);

    RegisterCommand("unhideglobal", "Unhides a player's @global name.", (c, a) =>
    {
        if (a.Length == 0) return false;
        string input = string.Join(" ", a);

        if (ulong.TryParse(input, out ulong id))
        {
            UnhideGlobalName(id);
            ForceRefreshAll();
            NotifyCaller(c, $"<color=green>[UnhideGlobal]</color> Unhid @global name for ID {id}.", Color.white);
            return true;
        }

        var target = FindPlayerByName(input);
        if (target != null && ulong.TryParse(target._steamID, out ulong sid))
        {
            UnhideGlobalName(sid);
            ForceRefreshAll();
            NotifyCaller(c, $"<color=green>[UnhideGlobal]</color> Unhid @global name for {target._nickname}.", Color.white);
        }
        else
        {
            NotifyCaller(c, $"<color=yellow>[UnhideGlobal]</color> Player not found. Use SteamID if hidden.", Color.white);
        }
        return true;
    }, client);
    RegisterCommand("blockhelp", "Shows all PModeration commands and tips.", (c, _) =>
    {
        NotifyCaller(c, "<color=#ff8800>=== PModeration Commands (all client-side) ===</color>", Color.white);

        NotifyCaller(c, "<color=white>/block <name></color> — Hide player (model, nameplate, audio)", Color.white);
        NotifyCaller(c, "<color=white>/unblock <name or SteamID></color> — Unhide player", Color.white);

        NotifyCaller(c, "<color=cyan>/hideglobal <name></color> — Hide only @global name (RP title stays)", Color.white);
        NotifyCaller(c, "<color=cyan>/unhideglobal <name or SteamID></color> — Unhide @global name", Color.white);

        NotifyCaller(c, "<color=white>/blocklist</color> — List all blocked SteamIDs", Color.white);
        NotifyCaller(c, "<color=white>/blockexport [optional path]</color> — Save blocklist to JSON", Color.white);
        NotifyCaller(c, "<color=white>/blockimport <path></color> — Load blocklist from JSON", Color.white);

        NotifyCaller(c, "<color=gray>Tip: Use SteamID with /unblock or /unhideglobal if the player is hidden and name not visible.</color>", Color.gray);
        NotifyCaller(c, "<color=gray>Config: Edit BepInEx/config/PModeration.cfg or use Mod Settings menu.</color>", Color.gray);

        return true;
    }, client);
}

        private static Player FindPlayerByName(string name) => FindObjectsOfType<Player>().FirstOrDefault(p => !p.isLocalPlayer && p._nickname.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    public static class PModerationAPI
    {
        public static bool IsPlayerBlocked(ulong steamID) => Plugin.Instance.IsBlocked(steamID);
        public static void BlockPlayer(ulong steamID) => Plugin.Instance.AddBlock(steamID);
        public static void UnblockPlayer(ulong steamID) => Plugin.Instance.RemoveBlock(steamID);
        public static void HideGlobalName(ulong steamID) => Plugin.Instance.HideGlobalName(steamID);
        public static void UnhideGlobalName(ulong steamID) => Plugin.Instance.UnhideGlobalName(steamID);
        public static void IsGlobalNameHidden(ulong steamID) => Plugin.Instance.IsGlobalNameHidden(steamID);
        public static event Action<ulong> OnPlayerBlocked;
        public static event Action<ulong> OnPlayerUnblocked;
        internal static void NotifyBlocked(ulong id) => OnPlayerBlocked?.Invoke(id);
        internal static void NotifyUnblocked(ulong id) => OnPlayerUnblocked?.Invoke(id);
    }

    [HarmonyPatch(typeof(ChatBehaviour), "UserCode_Rpc_RecieveChatMessage__String__Boolean__ChatChannel")]
    [HarmonyPriority(Priority.Last)]
    public static class ChatReceivePatch { static bool Prefix(ChatBehaviour __instance, ref string message) {
        if (!Plugin.CfgEnabled.Value || !Plugin.CfgCensorChat.Value) return true;
        try { var f = typeof(ChatBehaviour).GetField("_player", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if(f!=null) { var s = f.GetValue(__instance) as Player; if(s!=null && ulong.TryParse(s._steamID, out ulong id) && Plugin.Instance.IsBlocked(id)) message = Plugin.CfgCensorReplacement.Value; } } catch {} return true; } }
}
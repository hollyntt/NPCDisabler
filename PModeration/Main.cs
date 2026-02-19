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
using UnityEngine.UI;
using Newtonsoft.Json;

namespace PModeration
{
    [BepInPlugin(ModInfo.GUID, ModInfo.NAME, ModInfo.VERSION)]
    [BepInDependency("Soggy_Pancake.CommandLib")]
    [BepInDependency("EasySettings", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("CodeTalker")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance;
        internal static ManualLogSource Log;
        private readonly Harmony harmony = new Harmony(ModInfo.GUID);

        // --- CONFIG ---
        public static ConfigEntry<bool> CfgEnabled;
        public static ConfigEntry<bool> CfgHardBlock; // default false (soft block safer)
        public static ConfigEntry<bool> CfgCensorChat;
        public static ConfigEntry<string> CfgCensorReplacement;
        public static ConfigEntry<bool> CfgDebugMode;

        // --- DATA ---
        public static HashSet<ulong> BlockedSteamIDs = new HashSet<ulong>();
        private string blockListPath;

        private float visualRefreshTimer = 0f;
        private const float REFRESH_RATE = 1.0f;

        void Awake()
        {
            Instance = this;
            Log = Logger;
            blockListPath = Path.Combine(Paths.ConfigPath, "PModeration_BlockList.json");

            InitConfig();
            Settings.OnInitialized.AddListener(AddSettings);
            Settings.OnApplySettings.AddListener(() =>
            {
                Config.Save();
                ForceRefreshAll();
            });

            LoadBlockList();
            RegisterModCommands();

            harmony.PatchAll();

            Log.LogInfo($"{ModInfo.NAME} v{ModInfo.VERSION} Loaded.");
            Log.LogInfo("PModeration API is available for other mods: PModeration.PModerationAPI");
        }

        void Update()
        {
            if (!CfgEnabled.Value) return;

            visualRefreshTimer += Time.deltaTime;
            if (visualRefreshTimer >= REFRESH_RATE)
            {
                visualRefreshTimer = 0f;
                RefreshBlockedPlayersInScene();
            }
        }

        // ────────────────────────────────────────────────
        //  Commands
        // ────────────────────────────────────────────────
        private void RegisterModCommands()
        {
            var client = new CommandOptions { chatCommand = ChatCommandType.ClientSide };

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
                    NotifyCaller(c, $"<color=yellow>[Unblock]</color> Player not found. Use SteamID if hidden.",
                        Color.white);
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
            
            RegisterCommand("blockhelp", "Lists all PModeration commands.", (c, _) =>
            {
                NotifyCaller(c, "<color=red>PModeration Commands:</color>", Color.white);
                NotifyCaller(c, "<color=white>/block <name></color> — Locally hides a player by name.", Color.white);
                NotifyCaller(c, "<color=white>/unblock <name|steamID></color> — Unhides a player by name or SteamID.", Color.white);
                NotifyCaller(c, "<color=white>/blocklist</color> — Shows all currently blocked SteamIDs.", Color.white);
                NotifyCaller(c, "<color=white>/blockexport [path]</color> — Exports blocklist to a JSON file.", Color.white);
                NotifyCaller(c, "<color=white>/blockimport <path></color> — Imports blocklist from a JSON file.", Color.white);
                NotifyCaller(c, "<color=gray>Tip:</color> Use SteamID with /unblock if the player is hidden.", Color.white);
                return true;
            }, client);
        }

        // ────────────────────────────────────────────────
        //  Core Logic
        // ────────────────────────────────────────────────
        private void ProcessPlayer(Player p)
        {
            if (p == null || p.isLocalPlayer) return;
            if (!ulong.TryParse(p._steamID, out ulong sID)) return;

            bool shouldBlock = BlockedSteamIDs.Contains(sID) && CfgEnabled.Value;

            if (CfgHardBlock.Value)
            {
                // Hard: disable entire GameObject (includes network components)
                if (shouldBlock && p.gameObject.activeSelf)
                {
                    if (CfgDebugMode.Value) Log.LogInfo($"Hard-blocking {p._nickname} ({sID})");
                    p.gameObject.SetActive(false);
                }
                else if (!shouldBlock && !p.gameObject.activeSelf)
                {
                    if (CfgDebugMode.Value) Log.LogInfo($"Restoring {p._nickname} ({sID})");
                    p.gameObject.SetActive(true);
                }
            }
            else
            {
                // Soft: keep root active, toggle all direct children
                if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);

                foreach (Transform child in p.transform)
                    child.gameObject.SetActive(!shouldBlock);

                // Always keep audio at correct volume regardless
                foreach (var a in p.GetComponentsInChildren<AudioSource>(true))
                    a.volume = shouldBlock ? 0f : 1f;
            }
        }

        public void ForceRefreshAll()
        {
            foreach (var p in Resources.FindObjectsOfTypeAll<Player>())
            {
                if (p == null || !p.gameObject.scene.IsValid() || p.isLocalPlayer) continue;
                ProcessPlayer(p);
            }
        }

        private void RefreshBlockedPlayersInScene() => ForceRefreshAll();

        // ────────────────────────────────────────────────
        //  Block Management
        // ────────────────────────────────────────────────
        public void AddBlock(ulong steamID, string name = null)
        {
            if (BlockedSteamIDs.Add(steamID))
            {
                SaveBlockList();
                ForceRefreshAll();
                PModerationAPI.NotifyBlocked(steamID);
                if (CfgDebugMode.Value) Log.LogInfo($"Blocked {name ?? "unknown"} ({steamID})");
            }
        }

        public void RemoveBlock(ulong steamID)
        {
            if (BlockedSteamIDs.Remove(steamID))
            {
                SaveBlockList();
                ForceRefreshAll();
                PModerationAPI.NotifyUnblocked(steamID);
                if (CfgDebugMode.Value) Log.LogInfo($"Unblocked {steamID}");
            }
        }

        public bool IsBlocked(ulong steamID) => BlockedSteamIDs.Contains(steamID);

        // ────────────────────────────────────────────────
        //  Helpers
        // ────────────────────────────────────────────────
        private static Player FindPlayerByName(string name)
        {
            var players = Resources.FindObjectsOfTypeAll<Player>()
                .Where(p => p != null && p.gameObject.scene.IsValid() && !p.isLocalPlayer);

            var exact =
                players.FirstOrDefault(p => string.Equals(p._nickname, name, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            return players.FirstOrDefault(p => p._nickname.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // ────────────────────────────────────────────────
        //  Persistence
        // ────────────────────────────────────────────────
        private void LoadBlockList()
        {
            if (!File.Exists(blockListPath)) return;

            try
            {
                var json = File.ReadAllText(blockListPath);
                var list = JsonConvert.DeserializeObject<List<ulong>>(json);
                BlockedSteamIDs = new HashSet<ulong>(list ?? new List<ulong>());
                Log.LogInfo($"Loaded {BlockedSteamIDs.Count} blocked IDs.");
            }
            catch (Exception e)
            {
                Log.LogError($"Failed to load blocklist: {e.Message}");
            }
        }

        private void SaveBlockList()
        {
            try
            {
                var json = JsonConvert.SerializeObject(BlockedSteamIDs.ToList(), Formatting.Indented);
                File.WriteAllText(blockListPath, json);
            }
            catch (Exception e)
            {
                Log.LogError($"Failed to save blocklist: {e.Message}");
            }
        }

        // ────────────────────────────────────────────────
        //  Config & UI
        // ────────────────────────────────────────────────
        private void InitConfig()
        {
            CfgEnabled = Config.Bind("1. General", "Enabled", true, "Master switch");
            CfgHardBlock = Config.Bind("2. Blocking", "Hard Block (Advanced)", false,
                "True = disables entire player GameObject (thorough but riskier for party sync).\nFalse = soft block (only visuals/UI/audio – recommended default, safer)");
            CfgCensorChat = Config.Bind("3. Chat Censor", "Censor Chat", false,
                "Replace blocked players' chat with replacement text (optional – Atlyss mute already exists)");
            CfgCensorReplacement = Config.Bind("3. Chat Censor", "Replacement Text", "[BLOCKED]",
                "Text shown instead of blocked messages");
            CfgDebugMode = Config.Bind("4. Advanced", "Debug Mode", false, "Log detailed blocking actions");
        }

        private void AddSettings()
        {
            var tab = Settings.GetOrAddCustomTab(ModInfo.NAME);
            tab.AddHeader("General");
            tab.AddToggle(CfgEnabled);
            tab.AddHeader("Blocking");
            tab.AddToggle(CfgHardBlock);
            tab.AddHeader("Chat Filtering (optional)");
            tab.AddToggle(CfgCensorChat);
            tab.AddTextField(CfgCensorReplacement);
            tab.AddHeader("Advanced");
            tab.AddToggle(CfgDebugMode);
        }
    }

    // ────────────────────────────────────────────────
    //  PUBLIC API for other mods
    // ────────────────────────────────────────────────
    public static class PModerationAPI
    {
        /// <summary>
        /// Returns true if the given SteamID is blocked by the local player.
        /// </summary>
        public static bool IsPlayerBlocked(ulong steamID)
        {
            return Plugin.Instance.IsBlocked(steamID);
        }

        /// <summary>
        /// Adds a SteamID to the local block list and refreshes visuals.
        /// </summary>
        public static void BlockPlayer(ulong steamID)
        {
            Plugin.Instance.AddBlock(steamID);
        }

        /// <summary>
        /// Removes a SteamID from the local block list and refreshes visuals.
        /// </summary>
        public static void UnblockPlayer(ulong steamID)
        {
            Plugin.Instance.RemoveBlock(steamID);
        }

        /// <summary>
        /// Gets a read-only snapshot of currently blocked SteamIDs.
        /// </summary>
        public static IReadOnlyCollection<ulong> GetBlockedSteamIDs()
        {
            return Plugin.BlockedSteamIDs.ToList().AsReadOnly();
        }

        /// <summary>
        /// Event fired when a player is blocked (local only).
        /// </summary>
        public static event Action<ulong> OnPlayerBlocked;

        /// <summary>
        /// Event fired when a player is unblocked (local only).
        /// </summary>
        public static event Action<ulong> OnPlayerUnblocked;

        // Internal helpers to fire events
        internal static void NotifyBlocked(ulong id) => OnPlayerBlocked?.Invoke(id);
        internal static void NotifyUnblocked(ulong id) => OnPlayerUnblocked?.Invoke(id);
    }
    
    // ────────────────────────────────────────────────
    //  Harmony Patch – Chat Censor (optional)
    // ────────────────────────────────────────────────
    [HarmonyPatch(typeof(ChatBehaviour), "UserCode_Rpc_RecieveChatMessage__String__Boolean__ChatChannel")]
    public static class ChatReceivePatch
    {
        static bool Prefix(ChatBehaviour __instance, ref string message, bool _isEmoteMessage, ChatBehaviour.ChatChannel _chatChannel)
        {
            if (!Plugin.CfgEnabled.Value || !Plugin.CfgCensorChat.Value)
                return true;

            // Attempt to get sender safely without assuming field name
            Player sender = null;

            // If you know a reliable public property or method exists on ChatBehaviour,
            // use it here instead (preferred for future-proofing):
            // sender = __instance.Sender;  // ← example – replace with actual if exists

            // Fallback: try common private field names via reflection (only if needed)
            if (sender == null)
            {
                var possibleFields = new[] { "_player", "player", "m_Player", "_sender", "_owner" };
                foreach (var fieldName in possibleFields)
                {
                    var field = typeof(ChatBehaviour).GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (field != null)
                    {
                        sender = field.GetValue(__instance) as Player;
                        if (sender != null) break;
                    }
                }
            }

            if (sender == null)
            {
                if (Plugin.CfgDebugMode.Value)
                    Plugin.Log.LogWarning("Chat censor: Could not find sender Player instance.");
                return true;
            }

            if (!ulong.TryParse(sender._steamID, out ulong sID))
                return true;

            if (Plugin.Instance.IsBlocked(sID))
            {
                message = Plugin.CfgCensorReplacement.Value;
                if (Plugin.CfgDebugMode.Value)
                    Plugin.Log.LogInfo($"Censored chat message from blocked player {sender._nickname} ({sID})");
            }

            return true;
        }
    }
}
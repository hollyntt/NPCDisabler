using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AutoModeration
{
    // --- DATA CLASSES ---
    public class WarningRecord
    {
        public DateTime Timestamp { get; set; }
        public string PlayerName { get; set; }
        public string SteamID { get; set; }
        public string TriggeringMessage { get; set; }
        public int WarnCount { get; set; }
        public int MaxWarnings { get; set; }

        public override string ToString()
        {
            return $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] Player: {PlayerName} (ID: {SteamID}) | Warning {WarnCount}/{MaxWarnings} | Trigger: \"{TriggeringMessage}\"";
        }
    }

    public class BlockRule
    {
        public string Pattern { get; set; }
        public MatchType Type { get; set; }

        public bool IsMatch(string message)
        {
            switch (Type)
            {
                case MatchType.Contains:
                    return message.IndexOf(Pattern, StringComparison.OrdinalIgnoreCase) >= 0;
                case MatchType.StartsWith:
                    return message.StartsWith(Pattern, StringComparison.OrdinalIgnoreCase);
                case MatchType.EndsWith:
                    return message.EndsWith(Pattern, StringComparison.OrdinalIgnoreCase);
                case MatchType.Exact:
                    return Regex.IsMatch(message, @"\b" + Regex.Escape(Pattern) + @"\b", RegexOptions.IgnoreCase);
                default:
                    return false;
            }
        }
    }

    public enum MatchType { Contains, StartsWith, EndsWith, Exact }

    [BepInPlugin(ModInfo.GUID, ModInfo.NAME, ModInfo.VERSION)]
    public class Main : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static string WarningLogPath;
        
        // --- HOST LISTS (KICK/BAN) ---
        internal static List<BlockRule> HostBlockRules = new List<BlockRule>();
        internal static HashSet<string> HostBlockedHashes = new HashSet<string>();
        internal static List<Regex> HostRegexPatterns = new List<Regex>();

        // --- CLIENT LISTS (CENSOR ONLY) ---
        internal static List<BlockRule> ClientCensorRules = new List<BlockRule>();
        internal static HashSet<string> ClientCensorHashes = new HashSet<string>();
        
        internal static List<string> ParsedAllowedPhrases = new List<string>();
        internal static Dictionary<string, int> PlayerWarningLevels = new Dictionary<string, int>();
        internal static List<string> MonitoredChannels = new List<string>();
        internal static HashSet<string> TrustedSteamIDsList = new HashSet<string>();

        // --- CONFIGURATION ---
        internal static ConfigEntry<bool> AutoModEnabled;
        internal static ConfigEntry<bool> DisableInSinglePlayer;
        internal static ConfigEntry<string> MonitoredChatChannels;
        internal static ConfigEntry<string> TrustedUsers; 
        internal static ConfigEntry<string> AllowedPhrases;

        // Host Configs
        internal static ConfigEntry<string> HostBlockedWords;
        internal static ConfigEntry<string> HostRegexPatternsConfig;
        internal static ConfigEntry<bool> EnableHostActions;
        internal static ConfigEntry<string> HostAction;
        internal static ConfigEntry<bool> WarningSystemEnabled;
        internal static ConfigEntry<int> WarningsUntilAction;
        internal static ConfigEntry<bool> ResetWarningsOnDisconnect;

        // Client Configs
        internal static ConfigEntry<string> ClientCensoredWords;
        internal static ConfigEntry<string> CensorReplacementChar;

        private void Awake()
        {
            Log = Logger;
            string pluginFolder = Path.GetDirectoryName(Info.Location);
            WarningLogPath = Path.Combine(pluginFolder, "AutoMod_WarningLog.txt");

            // 1. General
            AutoModEnabled = Config.Bind("1. General", "Enabled", true, "Enables the mod functionality.");
            DisableInSinglePlayer = Config.Bind("1. General", "Disable in Single-Player", true, "Disable functionality in singleplayer.");
            MonitoredChatChannels = Config.Bind("1. General", "Monitored Channels", "GLOBAL", "Comma-separated list of chat channels to monitor.");
            
            // 2. Exceptions & Trust
            AllowedPhrases = Config.Bind("2. Exceptions", "Allowed Phrases (Whitelist)", "crypto, grapefruit, have a nice day", "Phrases that are always allowed (ignored by filter).");
            TrustedUsers = Config.Bind("2. Exceptions", "Trusted Steam IDs", "76561198000000000", "Steam IDs that bypass ALL filters (Host and Client).");

            // 3. HOST Filters (Kicks/Bans)
            HostBlockedWords = Config.Bind("3. Host Filters (Server-Side)", "Blocked Words", "*badword*, rude*, *insult", "Words that trigger Host Actions (Kick/Ban). Only active when YOU are Host.");
            HostRegexPatternsConfig = Config.Bind("3. Host Filters (Server-Side)", "Regex Patterns", "", "Regex patterns that trigger Host Actions.");
            EnableHostActions = Config.Bind("3. Host Filters (Server-Side)", "Enable Punishments", true, "If true, the host will kick/ban players matching the Host Filters.");
            HostAction = Config.Bind("3. Host Filters (Server-Side)", "Punishment Type", "Kick", new ConfigDescription("Action to take.", new AcceptableValueList<string>("Kick", "Ban")));
            WarningSystemEnabled = Config.Bind("3. Host Filters (Server-Side)", "Warning System", true, "Enable progressive warnings.");
            WarningsUntilAction = Config.Bind("3. Host Filters (Server-Side)", "Warnings Limit", 3, "Infractions before punishment.");
            ResetWarningsOnDisconnect = Config.Bind("3. Host Filters (Server-Side)", "Reset on Disconnect", true, "Clear warnings when player leaves.");

            // 4. CLIENT Filters (Censor Only)
            ClientCensoredWords = Config.Bind("4. Client Filters (Local Only)", "Censored Words", "heck, darn", "Words that will be replaced with **** locally. Active in ANY server.");
            CensorReplacementChar = Config.Bind("4. Client Filters (Local Only)", "Censor Character", "*", "The character used to hide words.");

            UpdateMonitoredChannelsList();
            UpdateAllowedPhrasesList();
            UpdateTrustedList();
            UpdateRuleLists(HostBlockedWords.Value, HostBlockRules, HostBlockedHashes);
            UpdateRuleLists(ClientCensoredWords.Value, ClientCensorRules, ClientCensorHashes);
            UpdateRegexList();

            Harmony.CreateAndPatchAll(typeof(HarmonyPatches));
            Log.LogInfo($"[{ModInfo.NAME} v{ModInfo.VERSION}] loaded.");
        }
        
        internal static string ComputeSha256Hash(string rawData)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2"));
                return builder.ToString();
            }
        }

        private void UpdateMonitoredChannelsList()
        {
            if (string.IsNullOrWhiteSpace(MonitoredChatChannels.Value)) MonitoredChannels.Clear();
            else MonitoredChannels = MonitoredChatChannels.Value.Split(',').Select(c => c.Trim().ToUpperInvariant()).Where(c => !string.IsNullOrEmpty(c)).ToList();
        }
        
        private void UpdateTrustedList()
        {
            TrustedSteamIDsList.Clear();
            if (string.IsNullOrWhiteSpace(TrustedUsers.Value)) return;
            foreach(var id in TrustedUsers.Value.Split(','))
            {
                string cleanId = id.Trim();
                if(!string.IsNullOrEmpty(cleanId)) TrustedSteamIDsList.Add(cleanId);
            }
        }

        private void UpdateAllowedPhrasesList()
        {
            if (string.IsNullOrWhiteSpace(AllowedPhrases.Value)) ParsedAllowedPhrases.Clear();
            else ParsedAllowedPhrases = AllowedPhrases.Value.Split(',').Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToList();
        }

        private void UpdateRuleLists(string configValue, List<BlockRule> rulesList, HashSet<string> hashesList)
        {
            rulesList.Clear();
            hashesList.Clear();
            if (string.IsNullOrWhiteSpace(configValue)) return;
            
            var patterns = configValue.Split(',');
            foreach (var pattern in patterns)
            {
                string trimmed = pattern.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                
                if (trimmed.Contains("*"))
                {
                    bool startsWithStar = trimmed.StartsWith("*");
                    bool endsWithStar = trimmed.EndsWith("*");
                    string corePattern = trimmed.Trim('*');
                    
                    if (startsWithStar && endsWithStar) rulesList.Add(new BlockRule { Pattern = corePattern, Type = MatchType.Contains });
                    else if (startsWithStar) rulesList.Add(new BlockRule { Pattern = corePattern, Type = MatchType.EndsWith });
                    else if (endsWithStar) rulesList.Add(new BlockRule { Pattern = corePattern, Type = MatchType.StartsWith });
                    else rulesList.Add(new BlockRule { Pattern = trimmed, Type = MatchType.Contains });
                }
                else 
                {
                    string hash = ComputeSha256Hash(trimmed.ToLowerInvariant());
                    hashesList.Add(hash);
                }
            }
        }

        private void UpdateRegexList()
        {
            HostRegexPatterns.Clear();
            if (string.IsNullOrWhiteSpace(HostRegexPatternsConfig.Value)) return;
            foreach (var pattern in HostRegexPatternsConfig.Value.Split(','))
            {
                try
                {
                    var trimmed = pattern.Trim();
                    if (!string.IsNullOrEmpty(trimmed)) HostRegexPatterns.Add(new Regex(trimmed, RegexOptions.Compiled | RegexOptions.IgnoreCase));
                }
                catch (Exception ex) { Log.LogError($"[AUTOMOD] Invalid Regex '{pattern}': {ex.Message}"); }
            }
        }
    }

    [HarmonyPatch]
    internal static class HarmonyPatches
    {
        [HarmonyPrefix, HarmonyPatch(typeof(ChatBehaviour), "UserCode_Rpc_RecieveChatMessage__String__Boolean__ChatChannel")]
        internal static bool InterceptChatMessage_Prefix(ChatBehaviour __instance, ref string message, bool _isEmoteMessage, ChatBehaviour.ChatChannel _chatChannel)
        {
            if (Main.DisableInSinglePlayer.Value && AtlyssNetworkManager._current != null && AtlyssNetworkManager._current._soloMode) return true;
            if (!Main.AutoModEnabled.Value || !Main.MonitoredChannels.Contains(_chatChannel.ToString().ToUpperInvariant())) return true;

            try
            {
                string plainTextMessage = Regex.Replace(message, "<color=#([0-9a-fA-F]{6})>|</color>", string.Empty);
                
                FieldInfo playerField = typeof(ChatBehaviour).GetField("_player", BindingFlags.Instance | BindingFlags.NonPublic);
                if (playerField != null && playerField.GetValue(__instance) is Player playerWhoSentMessage)
                {
                    if (playerWhoSentMessage._steamID != null && Main.TrustedSteamIDsList.Contains(playerWhoSentMessage._steamID)) return true;
                    if (playerWhoSentMessage._isHostPlayer) return true; 

                    string playerName = playerWhoSentMessage._nickname ?? "Unknown";
            
                    string messageToCheck = plainTextMessage;
                    foreach(string allowedPhrase in Main.ParsedAllowedPhrases)
                    {
                        messageToCheck = Regex.Replace(messageToCheck, Regex.Escape(allowedPhrase), string.Empty, RegexOptions.IgnoreCase);
                    }
                    
                    // HOST LOGIC (Kick/Ban/Warn)
                    if (Player._mainPlayer != null && Player._mainPlayer._isHostPlayer)
                    {
                        bool isHostInfraction = CheckList(messageToCheck, Main.HostBlockRules, Main.HostBlockedHashes, Main.HostRegexPatterns, out string hostReason);
                        if (isHostInfraction)
                        {
                            string logMessage = $"[AUTOMOD HOST] Infraction by [{playerName}] in [{_chatChannel}]. Reason: {hostReason}. Msg: \"{plainTextMessage}\"";
                            Main.Log.LogWarning(logMessage);
                            
                            ProcessHostInfraction(playerWhoSentMessage, playerName, plainTextMessage);
                            
                            return false; 
                        }
                    }

                    // CLIENT LOGIC (Censor)
                    bool isClientInfraction = CheckList(messageToCheck, Main.ClientCensorRules, Main.ClientCensorHashes, null, out string clientReason);
                    
                    if (isClientInfraction)
                    {
                        message = CensorMessage(message, messageToCheck); 
                        return true;
                    }
                }
            }
            catch (Exception ex) 
            { 
                Main.Log.LogError($"[AUTOMOD] Error in Chat Patch: {ex}"); 
            }
    
            return true;
        }

        private static bool CheckList(string message, List<BlockRule> rules, HashSet<string> hashes, List<Regex> regexes, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(message)) return false;

            foreach (BlockRule rule in rules)
            {
                if (rule.IsMatch(message)) 
                {
                    reason = $"rule '{rule.Pattern}'";
                    return true;
                }
            }
            
            if (hashes.Count > 0)
            {
                char[] delimiters = new char[] { ' ', '.', ',', '!', '?', ';', ':', '-', '_', '\n', '\r', '"', '\'' };
                string[] words = message.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);
                foreach (string word in words)
                {
                    if (string.IsNullOrWhiteSpace(word)) continue;
                    string wordHash = Main.ComputeSha256Hash(word.ToLowerInvariant());
                    if (hashes.Contains(wordHash))
                    {
                        reason = "hashed word";
                        return true;
                    }
                }
            }
            
            if (regexes != null)
            {
                foreach (Regex r in regexes)
                {
                    if (r.IsMatch(message)) 
                    {
                        reason = $"regex '{r}'";
                        return true;
                    }
                }
            }

            return false;
        }

        private static string CensorMessage(string originalMessage, string messageToCheck)
        {
            string censorCharStr = Main.CensorReplacementChar.Value;
            char censorChar = (string.IsNullOrEmpty(censorCharStr)) ? '*' : censorCharStr[0];
            string processed = originalMessage;

            foreach (BlockRule rule in Main.ClientCensorRules)
            {
                if (rule.IsMatch(processed))
                {
                     processed = Regex.Replace(processed, Regex.Escape(rule.Pattern), new string(censorChar, rule.Pattern.Length), RegexOptions.IgnoreCase);
                }
            }

            string[] parts = Regex.Split(processed, @"(\s+|[.,!?;:\-_""'])"); 
            StringBuilder sb = new StringBuilder();
            foreach (string part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                if (string.IsNullOrWhiteSpace(part) || part.Length < 2) 
                {
                    sb.Append(part);
                    continue;
                }

                string wordHash = Main.ComputeSha256Hash(part.ToLowerInvariant());
                if (Main.ClientCensorHashes.Contains(wordHash)) sb.Append(new string(censorChar, part.Length));
                else sb.Append(part);
            }
            return sb.ToString();
        }
        
        [HarmonyPostfix, HarmonyPatch(typeof(HostConsole), "Destroy_PeerListEntry")]
        internal static void OnPlayerDisconnect_Postfix(HostConsole __instance, int _connID)
        {
            if (!Main.ResetWarningsOnDisconnect.Value) return;

            try 
            {
                var entry = __instance._peerListEntries.FirstOrDefault(e => e._dataID == _connID);
                if (entry != null && entry._peerPlayer != null && !string.IsNullOrEmpty(entry._peerPlayer._steamID))
                {
                    if (Main.PlayerWarningLevels.ContainsKey(entry._peerPlayer._steamID))
                    {
                        Main.PlayerWarningLevels.Remove(entry._peerPlayer._steamID);
                        Main.Log.LogInfo($"[AUTOMOD] Cleared warnings for {entry._peerPlayer._nickname}");
                    }
                }
            }
            catch(Exception) { }
        }
        
        private static void ProcessHostInfraction(Player targetPlayer, string targetPlayerName, string triggeringMessage)
        {
            if (Player._mainPlayer == null || !Player._mainPlayer._isHostPlayer) return;

            string playerId = targetPlayer._steamID;
            if (string.IsNullOrEmpty(playerId)) return;

            if (!Main.PlayerWarningLevels.ContainsKey(playerId)) Main.PlayerWarningLevels[playerId] = 0;
            
            Main.PlayerWarningLevels[playerId]++;
            int currentWarnings = Main.PlayerWarningLevels[playerId];
            int maxWarnings = Main.WarningsUntilAction.Value;

            var record = new WarningRecord
            {
                Timestamp = DateTime.Now,
                PlayerName = targetPlayerName,
                SteamID = playerId,
                TriggeringMessage = triggeringMessage,
                WarnCount = currentWarnings,
                MaxWarnings = maxWarnings
            };
            SaveWarningToFile(record.ToString());
            
            // UPDATED: Use HostConsole.Init_ServerMessage to broadcast warning to everyone
            if (HostConsole._current != null)
            {
                string warnMsg = $"[AutoMod] Warning {currentWarnings}/{maxWarnings} for {targetPlayerName}.";
                HostConsole._current.Init_ServerMessage(warnMsg);
            }

            if (currentWarnings >= maxWarnings && Main.EnableHostActions.Value)
            {
                TakeHostAction(targetPlayer, targetPlayerName, triggeringMessage);
            }
        }
        
        private static void TakeHostAction(Player targetPlayer, string targetPlayerName, string triggeringMessage)
        {
            if (HostConsole._current == null || targetPlayer.connectionToClient == null) return;
            
            string action = Main.HostAction.Value.ToLower();
            string punishmentDetails = $"Player {targetPlayerName} (ID: {targetPlayer._steamID}) was automatically {action.ToUpper()}ED. Reason: {triggeringMessage}";

            Main.Log.LogWarning(punishmentDetails);
            
            try { File.AppendAllText(Main.WarningLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [PUNISHMENT] " + punishmentDetails + Environment.NewLine); }
            catch (Exception ex) { Main.Log.LogError($"Failed to write punishment to log: {ex.Message}"); }

            // Broadcast punishment to everyone
            HostConsole._current.Init_ServerMessage($"[AutoMod] {targetPlayerName} has been {action}ed for offensive language.");

            if (action == "ban")
            {
                HC_PeerListEntry targetPeer = null;
                foreach(var entry in HostConsole._current._peerListEntries)
                {
                    if (entry._netId != null && entry._netId.netId == targetPlayer.netId) 
                    { 
                        targetPeer = entry; 
                        break; 
                    }
                }
                        
                if (targetPeer != null)
                {
                    HostConsole._current._selectedPeerEntry = targetPeer;
                    HostConsole._current.Ban_Peer();
                }
                else 
                {
                    targetPlayer.connectionToClient.Disconnect();
                }
            }
            else
            {
                targetPlayer.connectionToClient.Disconnect();
            }
        }

        private static void SaveWarningToFile(string message)
        {
            try { File.AppendAllText(Main.WarningLogPath, message + Environment.NewLine); }
            catch (Exception) { }
        }
    }
}
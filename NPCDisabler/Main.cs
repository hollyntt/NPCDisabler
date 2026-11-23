using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Nessie.ATLYSS.EasySettings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NPCDisabler
{
    [BepInPlugin(ModInfo.GUID, ModInfo.NAME, ModInfo.VERSION)]
    [BepInDependency("Nessie.ATLYSS.EasySettings", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance;
        internal static ManualLogSource Log;
        private readonly Harmony harmony = new Harmony(ModInfo.GUID);

        private const string NpcSection = "NPCs";
        private static readonly char[] InvalidKeyChars = { '=', '\n', '\t', '\\', '"', '\'', '[', ']' };

        private static readonly Dictionary<string, ConfigEntry<bool>> NpcToggles = new Dictionary<string, ConfigEntry<bool>>();
        private static ConfigEntry<bool> DisableAllNpcs;

        private static readonly Dictionary<string, List<GameObject>> NpcRegistry = new Dictionary<string, List<GameObject>>();
        private static readonly Dictionary<string, bool> LastAppliedHiddenState = new Dictionary<string, bool>();

        private float _rescanTimer;
        private const float RescanInterval = 5f;
        private bool _settingsInitialized;
        private bool _globalToggleAdded;
        private SettingsTab _modTab;
        private Scene _currentTargetScene;
        private readonly HashSet<string> _npcNamesAddedToTab = new HashSet<string>();

        void Awake()
        {
            Instance = this;
            Log = Logger;
            harmony.PatchAll();

            DisableAllNpcs = Config.Bind("Global", "Disable All NetNPCs", false,
                "Hides every NetNPC discovered in your current map instance, client-side only. " +
                "Overrides individual NPC toggles below while enabled.");
            DisableAllNpcs.SettingChanged += (s, e) => ReapplyHiddenState();

            // Pre-bind NPCs saved from previous sessions (or hand-edited into the config)
            // so their toggles exist before EasySettings builds the menu.
            PreBindKnownNpcs();

            Log.LogInfo($"{ModInfo.NAME} v{ModInfo.VERSION} Loaded. ({NpcToggles.Count} known NPC(s) from config)");

            SceneManager.sceneLoaded += OnSceneLoaded;
            InitEasySettingsSafe();
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void Update()
        {
            Scene newTargetScene = GetCurrentMapScene();

            if (newTargetScene != _currentTargetScene)
            {
                _currentTargetScene = newTargetScene;
                NpcRegistry.Clear();
                LastAppliedHiddenState.Clear();
                ScanForNetNPCs();
            }

            _rescanTimer += Time.deltaTime;
            if (_rescanTimer < RescanInterval) return;

            _rescanTimer = 0f;
            ScanForNetNPCs();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _currentTargetScene = default;
        }

        // ---------- Config helpers ----------

        private static string SanitizeKey(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unknown";
            string cleaned = string.Concat(name.Select(c => InvalidKeyChars.Contains(c) ? '_' : c)).Trim();
            return cleaned.Length == 0 ? "Unknown" : cleaned;
        }

        // Reads the [NPCs] section straight from the config file on disk.
        private void PreBindKnownNpcs()
        {
            try
            {
                string path = Config.ConfigFilePath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

                var names = new List<string>();
                bool inNpcSection = false;

                foreach (var raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        inNpcSection = line.Substring(1, line.Length - 2) == NpcSection;
                        continue;
                    }

                    if (!inNpcSection) continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    names.Add(line.Substring(0, eq).Trim());
                }

                foreach (var name in names)
                    EnsureNpcEntry(name);
            }
            catch (Exception e)
            {
                Log.LogWarning($"Failed to pre-bind NPCs from config: {e.Message}");
            }
        }

        // Returns true if a new entry was created.
        private bool EnsureNpcEntry(string npcName)
        {
            if (NpcToggles.ContainsKey(npcName)) return false;

            ConfigEntry<bool> entry;
            try
            {
                entry = Config.Bind(NpcSection, npcName, false,
                    $"Hide '{npcName}' client-side when encountered. Does not affect other players. " +
                    "Overridden by 'Disable All NetNPCs' above while that's enabled.");
            }
            catch (Exception e)
            {
                Log.LogWarning($"Couldn't bind config for NPC '{npcName}': {e.Message}");
                return false;
            }

            NpcToggles[npcName] = entry;
            entry.SettingChanged += (s, e) => SetNpcHidden(npcName, IsEffectivelyHidden(npcName));
            return true;
        }

        // ---------- EasySettings ----------

        private void InitEasySettingsSafe()
        {
            Settings.OnInitialized.AddListener(OnSettingsInitialized);
            Settings.OnApplySettings.AddListener(OnSettingsApplied);
        }

        private void OnSettingsInitialized()
        {
            _settingsInitialized = true;
            RefreshSettingsTab();
        }

        private void OnSettingsApplied()
        {
            Config.Save();
            ReapplyHiddenState();
            Log.LogInfo($"[{ModInfo.NAME}] Settings applied and saved.");
        }

        private void RefreshSettingsTab()
        {
            if (_modTab == null)
                _modTab = Settings.GetOrAddCustomTab(ModInfo.NAME);

            if (_modTab == null)
            {
                Log.LogWarning("Failed to get or add EasySettings tab.");
                return;
            }

            if (!_globalToggleAdded)
            {
                _modTab.AddHeader("Global");
                _modTab.AddToggle("Disable All NetNPCs", DisableAllNpcs);
                _globalToggleAdded = true;
            }

            var newEntries = NpcToggles
                .Where(kvp => !_npcNamesAddedToTab.Contains(kvp.Key))
                .OrderBy(kvp => kvp.Key)
                .ToList();

            if (newEntries.Count > 0 && _npcNamesAddedToTab.Count == 0)
                _modTab.AddHeader("NPCs");

            foreach (var kvp in newEntries)
            {
                try
                {
                    _modTab.AddToggle(kvp.Key, kvp.Value);
                    _npcNamesAddedToTab.Add(kvp.Key);
                }
                catch (Exception e)
                {
                    Log.LogWarning($"Failed to add toggle for '{kvp.Key}': {e.Message}");
                }
            }
        }

        // ---------- Scene / NPC scanning ----------

        private Scene GetCurrentMapScene()
        {
            if (Player._mainPlayer != null && Player._mainPlayer.Network_playerMapInstance != null)
            {
                Scene assignedScene = Player._mainPlayer.Network_playerMapInstance.gameObject.scene;
                if (assignedScene.isLoaded)
                    return assignedScene;
            }

            if (Player._mainPlayer != null)
            {
                Scene playerScene = Player._mainPlayer.gameObject.scene;
                if (playerScene.isLoaded && playerScene.name != "DontDestroyOnLoad")
                    return playerScene;
            }

            return SceneManager.GetActiveScene();
        }

        private void ScanForNetNPCs()
        {
            Scene targetScene = GetCurrentMapScene();
            if (!targetScene.isLoaded) return;

            MapInstance targetInstance = Player._mainPlayer != null
                ? Player._mainPlayer.Network_playerMapInstance
                : null;

            var allNetNpcsUnfiltered = FindObjectsOfType<NetNPC>(true);

            string playerSceneInfo = "null";
            string assignedInstanceInfo = "null";
            if (Player._mainPlayer != null)
            {
                Scene ps = Player._mainPlayer.gameObject.scene;
                playerSceneInfo = $"{ps.name} (handle={ps.handle})";

                if (targetInstance != null)
                {
                    Scene ms = targetInstance.gameObject.scene;
                    assignedInstanceInfo = $"{ms.name} (handle={ms.handle}, instanceID={targetInstance.GetInstanceID()})";
                }
            }

            Log.LogDebug($"[DEBUG] targetScene={targetScene.name} (handle={targetScene.handle}) | " +
                        $"playerScene={playerSceneInfo} | assignedMapInstance={assignedInstanceInfo} | " +
                        $"totalNetNPCsInMemory={allNetNpcsUnfiltered.Length}");

            var netNpcs = allNetNpcsUnfiltered.Where(npc =>
            {
                if (targetInstance != null)
                {
                    MapInstance npcInstance = npc.GetComponentInParent<MapInstance>(true);
                    if (npcInstance != null)
                        return npcInstance == targetInstance;
                }
                return npc.gameObject.scene == targetScene;
            });

            bool discoveredNew = false;

            foreach (var npc in netNpcs)
            {
                string npcName = GetNpcIdentifier(npc);

                if (!NpcRegistry.TryGetValue(npcName, out var list))
                {
                    list = new List<GameObject>();
                    NpcRegistry[npcName] = list;
                }

                if (!list.Contains(npc.gameObject))
                    list.Add(npc.gameObject);

                if (EnsureNpcEntry(npcName))
                    discoveredNew = true;
            }

            if (discoveredNew)
            {
                Log.LogInfo($"[{ModInfo.NAME}] Discovered new NPC type(s); {NpcToggles.Count} known in total. Scene: {targetScene.name}");
                if (_settingsInitialized)
                    RefreshSettingsTab();
            }

            ReapplyHiddenState();
        }

        private string GetNpcIdentifier(NetNPC npc)
        {
            return SanitizeKey(npc.gameObject.name.Replace("(Clone)", string.Empty).Trim());
        }

        private bool IsEffectivelyHidden(string npcName)
        {
            if (DisableAllNpcs.Value) return true;
            return NpcToggles.TryGetValue(npcName, out var entry) && entry.Value;
        }

        private void SetNpcHidden(string npcName, bool hide)
        {
            if (!NpcRegistry.TryGetValue(npcName, out var instances)) return;

            if (LastAppliedHiddenState.TryGetValue(npcName, out bool lastState) && lastState == hide)
                return;

            foreach (var go in instances)
            {
                if (go == null) continue;
                ApplyHiddenState(go, hide);
            }

            LastAppliedHiddenState[npcName] = hide;

            Log.LogInfo($"[{ModInfo.NAME}] {(hide ? "Hid" : "Restored")} '{npcName}' " +
                        $"({instances.Count} instance(s)).");
        }

        private void ApplyHiddenState(GameObject npcGo, bool hide)
        {
            bool isHost = Player._mainPlayer != null && Player._mainPlayer._isHostPlayer;

            if (!isHost)
            {
                npcGo.SetActive(!hide);
                return;
            }

            foreach (var renderer in npcGo.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = !hide;

            foreach (var canvas in npcGo.GetComponentsInChildren<Canvas>(true))
                canvas.enabled = !hide;

            foreach (var canvasGroup in npcGo.GetComponentsInChildren<CanvasGroup>(true))
                canvasGroup.enabled = !hide;

            foreach (var animator in npcGo.GetComponentsInChildren<Animator>(true))
                animator.enabled = !hide;

            foreach (var collider in npcGo.GetComponentsInChildren<Collider>(true))
                collider.enabled = !hide;
        }

        private void ReapplyHiddenState()
        {
            foreach (var npcName in NpcRegistry.Keys.ToList())
            {
                SetNpcHidden(npcName, IsEffectivelyHidden(npcName));
            }
        }
    }
}
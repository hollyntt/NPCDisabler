using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Nessie.ATLYSS.EasySettings;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NPCDisabler
{
    [BepInPlugin(ModInfo.GUID, ModInfo.NAME, ModInfo.VERSION)]
    [BepInDependency("Nessie.ATLYSS.EasySettings", BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance;
        internal static ManualLogSource Log;
        private readonly Harmony harmony = new Harmony(ModInfo.GUID);

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

            Log.LogInfo($"{ModInfo.NAME} v{ModInfo.VERSION} Loaded.");

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

        private void InitEasySettingsSafe()
        {
            Settings.OnInitialized.AddListener(OnSettingsInitialized);
            Settings.OnApplySettings.AddListener(OnSettingsApplied);
        }

        private void OnSettingsInitialized()
        {
            _settingsInitialized = true;

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

            foreach (var kvp in newEntries)
            {
                _modTab.AddToggle(kvp.Key, kvp.Value);
                _npcNamesAddedToTab.Add(kvp.Key);
            }
        }

        private void OnSettingsApplied()
        {
            Config.Save();
            ReapplyHiddenState();
            Log.LogInfo($"[{ModInfo.NAME}] Settings applied and saved.");
        }

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

            var allNetNpcsUnfiltered = FindObjectsOfType<NetNPC>(true);
            Log.LogDebug($"[DEBUG] targetScene={targetScene.name} (handle={targetScene.handle}) | " +
                        $"playerScene={playerSceneInfo} | assignedMapInstance={assignedInstanceInfo} | " +
                        $"totalNetNPCsInMemory={allNetNpcsUnfiltered.Length}");

            foreach (var npc in allNetNpcsUnfiltered)
            {
                Scene npcScene = npc.gameObject.scene;
                MapInstance npcInstance = npc.GetComponentInParent<MapInstance>(true);
                bool sceneMatches = npcScene == targetScene;
                bool instanceMatches = npcInstance != null && targetInstance != null && npcInstance == targetInstance;
                string npcInstanceInfo = npcInstance != null
                    ? $"instanceID={npcInstance.GetInstanceID()}"
                    : "NOT PARENTED under a MapInstance";
                Log.LogDebug($"[DEBUG]   NPC '{GetNpcIdentifier(npc)}' in scene {npcScene.name} " +
                            $"(handle={npcScene.handle}) | parentMapInstance: {npcInstanceInfo} | " +
                            $"sceneMatch={sceneMatches} instanceMatch={instanceMatches}");
            }

            var netNpcs = FindObjectsOfType<NetNPC>(true)
                .Where(npc =>
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

                if (!NpcToggles.ContainsKey(npcName))
                {
                    var entry = Config.Bind("NPCs", npcName, false,
                        $"Hide '{npcName}' client-side when encountered. Does not affect other players. " +
                        "Overridden by 'Disable All NetNPCs' above while that's enabled.");
                    NpcToggles[npcName] = entry;
                    entry.SettingChanged += (s, e) => SetNpcHidden(npcName, IsEffectivelyHidden(npcName));
                    discoveredNew = true;

                    if (_settingsInitialized && _modTab != null && !_npcNamesAddedToTab.Contains(npcName))
                    {
                        _modTab.AddToggle(npcName, entry);
                        _npcNamesAddedToTab.Add(npcName);
                    }
                }
            }

            if (discoveredNew)
            {
                Log.LogInfo($"[{ModInfo.NAME}] Discovered {NpcToggles.Count} unique NPC type(s) in scene: {targetScene.name}");
            }

            ReapplyHiddenState();
        }

        private string GetNpcIdentifier(NetNPC npc)
        {
            return npc.gameObject.name.Replace("(Clone)", string.Empty).Trim();
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
            foreach (var npcName in NpcRegistry.Keys)
            {
                SetNpcHidden(npcName, IsEffectivelyHidden(npcName));
            }
        }
    }
}
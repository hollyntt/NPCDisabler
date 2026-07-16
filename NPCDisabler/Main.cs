using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Nessie.ATLYSS.EasySettings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NPCDisabler
{
    [BepInPlugin(ModInfo.GUID, ModInfo.NAME, ModInfo.VERSION)]
    [BepInDependency(Plugin.EasySettingsGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        internal const string EasySettingsGuid = "Nessie.ATLYSS.EasySettings";

        public static Plugin Instance;
        internal static ManualLogSource Log;
        private readonly Harmony harmony = new Harmony(ModInfo.GUID);

        internal static readonly Dictionary<string, ConfigEntry<bool>> NpcToggles = new Dictionary<string, ConfigEntry<bool>>();
        internal static ConfigEntry<bool> DisableAllNpcs;

        private static readonly Dictionary<string, List<GameObject>> NpcRegistry = new Dictionary<string, List<GameObject>>();
        private static readonly Dictionary<string, bool> LastAppliedHiddenState = new Dictionary<string, bool>();

        private float _rescanTimer;
        private const float RescanInterval = 5f;
        internal static bool SettingsInitialized;
        private static bool _easySettingsAvailable;
        private Scene _currentTargetScene;

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

            _easySettingsAvailable = Chainloader.PluginInfos.ContainsKey(EasySettingsGuid);
            if (_easySettingsAvailable)
                EasySettingsIntegration.Init();
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
            Log.LogDebug($"[{ModInfo.NAME}][DEBUG] targetScene={targetScene.name} (handle={targetScene.handle}) | " +
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
                Log.LogDebug($"[{ModInfo.NAME}][DEBUG]   NPC '{GetNpcIdentifier(npc)}' in scene {npcScene.name} " +
                            $"(handle={npcScene.handle}) | parentMapInstance: {npcInstanceInfo} | " +
                            $"sceneMatch={sceneMatches} instanceMatch={instanceMatches}");
            }

            var netNpcs = allNetNpcsUnfiltered
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

                    if (_easySettingsAvailable && SettingsInitialized)
                        EasySettingsIntegration.AddNpcToggle(npcName, entry);
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

        private static bool IsEffectivelyHidden(string npcName)
        {
            if (DisableAllNpcs.Value) return true;
            return NpcToggles.TryGetValue(npcName, out var entry) && entry.Value;
        }

        private static void SetNpcHidden(string npcName, bool hide)
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

        private static void ApplyHiddenState(GameObject npcGo, bool hide)
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
        }

        internal static void ReapplyHiddenState()
        {
            foreach (var npcName in NpcRegistry.Keys)
            {
                SetNpcHidden(npcName, IsEffectivelyHidden(npcName));
            }
        }
    }

    internal static class EasySettingsIntegration
    {
        private static SettingsTab _modTab;
        private static bool _globalToggleAdded;
        private static readonly HashSet<string> _npcNamesAddedToTab = new HashSet<string>();

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void Init()
        {
            try
            {
                Settings.OnInitialized.AddListener(OnSettingsInitialized);
                Settings.OnApplySettings.AddListener(OnSettingsApplied);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Failed to register with EasySettings! Please report this to the mod author!");
                Plugin.Log.LogWarning($"Exception: {e}");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static void OnSettingsInitialized()
        {
            try
            {
                Plugin.SettingsInitialized = true;

                if (_modTab == null)
                    _modTab = Settings.GetOrAddCustomTab(ModInfo.NAME);

                if (_modTab == null)
                {
                    Plugin.Log.LogWarning("Failed to get or add EasySettings tab.");
                    return;
                }

                if (!_globalToggleAdded)
                {
                    _modTab.AddHeader("Global");
                    _modTab.AddToggle("Disable All NetNPCs", Plugin.DisableAllNpcs);
                    _globalToggleAdded = true;
                }

                var newEntries = Plugin.NpcToggles
                    .Where(kvp => !_npcNamesAddedToTab.Contains(kvp.Key))
                    .OrderBy(kvp => kvp.Key)
                    .ToList();

                foreach (var kvp in newEntries)
                {
                    _modTab.AddToggle(kvp.Key, kvp.Value);
                    _npcNamesAddedToTab.Add(kvp.Key);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Failed to initialize EasySettings tab! Please report this to the mod author!");
                Plugin.Log.LogWarning($"Exception: {e}");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static void OnSettingsApplied()
        {
            try
            {
                Plugin.Instance.Config.Save();
                Plugin.ReapplyHiddenState();
                Plugin.Log.LogInfo($"[{ModInfo.NAME}] Settings applied and saved.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Failed to apply EasySettings changes! Please report this to the mod author!");
                Plugin.Log.LogWarning($"Exception: {e}");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void AddNpcToggle(string npcName, ConfigEntry<bool> entry)
        {
            try
            {
                if (_modTab == null || _npcNamesAddedToTab.Contains(npcName))
                    return;

                _modTab.AddToggle(npcName, entry);
                _npcNamesAddedToTab.Add(npcName);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Failed to add EasySettings toggle for '{npcName}'! Please report this to the mod author!");
                Plugin.Log.LogWarning($"Exception: {e}");
            }
        }
    }
}
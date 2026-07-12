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
    [BepInDependency("Nessie.ATLYSS.EasySettings", BepInDependency.DependencyFlags.SoftDependency)]
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

            // Only rescan if the target map scene has actually changed
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
            // Trigger a scene re-evaluation when a new scene loads
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

            // GetOrAddCustomTab is called exactly once, here. It's idempotent
            // (returns the existing tab by name if one's already registered), but
            // there's no reason to keep re-querying it - cache the reference and
            // reuse it everywhere else (see ScanForNetNPCs).
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

        /// <summary>
        /// Reliably determines the actual map scene to scan. Uses the player's
        /// server-assigned MapInstance (Network_playerMapInstance) as the source of
        /// truth, since multiple instances of the same map can be loaded
        /// simultaneously on a host - a first-match FindObjectsOfType scan over all
        /// loaded MapInstances can't tell which one the local player is actually in.
        /// </summary>
        private Scene GetCurrentMapScene()
        {
            // Priority 1: the instance the game itself has assigned this player to.
            // MapInstance.Handle_IteratePopulationList sets this every frame for every
            // peer, so it's authoritative even when several instances of the same map
            // are loaded at once.
            if (Player._mainPlayer != null && Player._mainPlayer.Network_playerMapInstance != null)
            {
                Scene assignedScene = Player._mainPlayer.Network_playerMapInstance.gameObject.scene;
                if (assignedScene.isLoaded)
                    return assignedScene;
            }

            // Priority 2: fallback to the player's own scene (e.g. main menu, or
            // before a MapInstance has assigned itself yet).
            if (Player._mainPlayer != null)
            {
                Scene playerScene = Player._mainPlayer.gameObject.scene;
                if (playerScene.isLoaded && playerScene.name != "DontDestroyOnLoad")
                    return playerScene;
            }

            // Priority 3: ultimate fallback to the currently active scene.
            return SceneManager.GetActiveScene();
        }

        private void ScanForNetNPCs()
        {
            Scene targetScene = GetCurrentMapScene();
            if (!targetScene.isLoaded) return;

            // --- TEMP DIAGNOSTIC LOGGING ---
            // Scene names aren't unique on additive loads, so two different loaded
            // scenes can both display as "_zone00_sanctum" while being distinct
            // Scene structs. .handle IS unique per loaded scene instance, so that's
            // what actually proves a match or mismatch. This logs enough to compare
            // player scene vs assigned MapInstance scene vs where NetNPCs actually
            // live - remove once the scoping is confirmed correct.
            {
                string playerSceneInfo = "null";
                string assignedInstanceInfo = "null";
                if (Player._mainPlayer != null)
                {
                    Scene ps = Player._mainPlayer.gameObject.scene;
                    playerSceneInfo = $"{ps.name} (handle={ps.handle})";

                    if (Player._mainPlayer.Network_playerMapInstance != null)
                    {
                        Scene ms = Player._mainPlayer.Network_playerMapInstance.gameObject.scene;
                        assignedInstanceInfo = $"{ms.name} (handle={ms.handle})";
                    }
                }

                var allNetNpcsUnfiltered = FindObjectsOfType<NetNPC>(true);
                Log.LogInfo($"[{ModInfo.NAME}][DEBUG] targetScene={targetScene.name} (handle={targetScene.handle}) | " +
                            $"playerScene={playerSceneInfo} | assignedMapInstanceScene={assignedInstanceInfo} | " +
                            $"totalNetNPCsInMemory={allNetNpcsUnfiltered.Length}");

                foreach (var npc in allNetNpcsUnfiltered)
                {
                    Scene npcScene = npc.gameObject.scene;
                    bool matches = npcScene == targetScene;
                    Log.LogInfo($"[{ModInfo.NAME}][DEBUG]   NPC '{GetNpcIdentifier(npc)}' in scene {npcScene.name} " +
                                $"(handle={npcScene.handle}) - {(matches ? "MATCHES target" : "does NOT match target")}");
                }
            }
            // --- END TEMP DIAGNOSTIC LOGGING ---

            // STRICTLY find NetNPCs within the target map scene
            var netNpcs = FindObjectsOfType<NetNPC>(true)
                .Where(npc => npc.gameObject.scene == targetScene);

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

                    // Dynamically add newly discovered NPCs to the EasySettings tab immediately
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

            // Host-safe local hiding: keeps the NetworkBehaviour and GameObject active
            // so server-driven logic (navigation, SyncVars) keeps running for every
            // client, but removes local visibility.
            //
            // Collider is intentionally left untouched here: on a listen-server host,
            // the collider is part of the same physics scene the server uses for
            // interaction hit-testing. Disabling it risks removing the NPC from
            // server-side queries entirely (e.g. dialog trigger raycasts), which would
            // break interaction for OTHER players too - not just hide it locally.
            // Needs a live multiplayer test (host hides NPC, second client tries to
            // interact with it) before this is touched.
            foreach (var renderer in npcGo.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = !hide;

            foreach (var canvas in npcGo.GetComponentsInChildren<Canvas>(true))
                canvas.enabled = !hide;

            foreach (var canvasGroup in npcGo.GetComponentsInChildren<CanvasGroup>(true))
                canvasGroup.enabled = !hide;

            foreach (var animator in npcGo.GetComponentsInChildren<Animator>(true))
                animator.enabled = !hide;
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
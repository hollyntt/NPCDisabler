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

namespace NPCDisabler
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

        void Awake()
        {
            Instance = this;
            Log = Logger;
            harmony.PatchAll();
            Log.LogInfo($"{ModInfo.NAME} v{ModInfo.VERSION} Loaded.");
        }

        void Update()
        {

        }
    }
}
﻿using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using Deli;
using UnityEngine.SceneManagement;
using WurstMod.Runtime.ScenePatchers;

namespace WurstMod.Runtime
{
    public class Entrypoint : DeliMod
    {
        void Awake()
        {
            LegacySupport.Init();
            RegisterListeners();
            InitDetours();
            InitAppDomain();
            InitConfig();
            CustomLevelFinder.DiscoverLevelsInFolder();
        }

        void RegisterListeners()
        {
            SceneManager.sceneLoaded += SceneManager_sceneLoaded;
        }

        void InitDetours()
        {
            Patches.Patch();
        }

        private static readonly Dictionary<string, Assembly> Assemblies = new Dictionary<string, Assembly>();

        void InitAppDomain()
        {
            AppDomain.CurrentDomain.AssemblyLoad += (sender, e) => { Assemblies[e.LoadedAssembly.FullName] = e.LoadedAssembly; };
            AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
            {
                Assemblies.TryGetValue(e.Name, out var assembly);
                return assembly;
            };
        }

        public static ConfigEntry<string> ConfigQuickload;
        public static ConfigEntry<bool> LoadDebugLevels;
        void InitConfig()
        {
            ConfigQuickload = BaseMod.Config.Bind("Debug", "QuickloadPath", "", "Set this to a folder containing the scene you would like to load as soon as H3VR boots. This is good for quickly testing scenes you are developing.");
            LoadDebugLevels = BaseMod.Config.Bind("Debug", "LoadDebugLevels", true, "True if you want the included default levels to be loaded");
        }

        private void SceneManager_sceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TNH_LevelSelector.SetupLevelSelector(scene);
            Generic_LevelPopulator.SetupLevelPopulator(scene);
            
            StartCoroutine(Loader.OnSceneLoad(scene));
            DebugQuickloader.Quickload(scene); // Must occur after regular loader.
        }
    }
}
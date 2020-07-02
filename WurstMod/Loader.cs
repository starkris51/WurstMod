﻿using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using WurstMod.TNH.Extras;
using FistVR;
using Valve.VR.InteractionSystem;
using UnityEngine.Rendering;
using WurstMod.Any;

namespace WurstMod
{
    public static class Loader
    {
        private static readonly List<string> whitelistedObjectsTNH = new List<string>()
        {
            "[BullshotCamera]",
            "[CameraRig]Fixed",
            "[SceneSettings]",
            "_AIManager_TNH_Indoors",
            "_CoverPointSystem",
            "_FinalScore",
            "_GameManager",
            "_ItemSpawner",
            "_NewTAHReticle",
            "_ReverbSystem",
            "EventSystem",
            "[FXPoolManager](Clone)",
            "MuzzleFlash_AlloyLight(Clone)"
        };
        private static readonly List<string> whitelistedObjectsGeneric = new List<string>()
        {
            "[BullshotCamera]",
            "[CameraRig]Fixed",
            "[SceneSettings_IndoorRange]",
            "[SteamVR]",
            "_AIManager",
            "_AmbientAudio",
            "_CoverPointManager",
            "_FinalScore",
            "_ReverbSystem",
            "BangerDetonator",
            "Destructobin",
            "ItemSpawner",
            "SosigSpawner",
            "SosigTestingPanels",
            "WhizzBangADinger2"

        };
        private static readonly List<string> blacklistedObjectsTNH = new List<string>()
        {
            "HoldPoint_0",
            "HoldPoint_1",
            "HoldPoint_2",
            "HoldPoint_3",
            "HoldPoint_4",
            "HoldPoint_5",
            "HoldPoint_6",
            "HoldPoint_7",
            "HoldPoint_8",
            "HoldPoint_9",
            "HoldPoint_10",
            "HoldPoint_11",
            "HoldPoint_12",
            "Ladders",
            "Lighting_Greenhalls",
            "Lighting_Hallways",
            "Lighting_HoldRooms",
            "Lighting_SupplyRooms",
            "OpenArea",
            "RampHelperCubes",
            "ReflectionProbes",
            "SupplyPoint_0",
            "SupplyPoint_1",
            "SupplyPoint_2",
            "SupplyPoint_3",
            "SupplyPoint_4",
            "SupplyPoint_5",
            "SupplyPoint_6",
            "SupplyPoint_7",
            "SupplyPoint_8",
            "SupplyPoint_9",
            "SupplyPoint_10",
            "SupplyPoint_11",
            "SupplyPoint_12",
            "SupplyPoint_13",
            "Tiles"
        };
        private static readonly List<string> blacklistedObjectsGeneric = new List<string>()
        {
            "_Animator_Spawning_Red",
            "_Animator_Spawning_Blue",
            "_Boards",
            "_Env",
            "AILadderTest1",
            "AILadderTest1 (1)",
            "AILadderTest1 (2)",
            "AILadderTest1 (3)"
        };


        // Marks which level we are loading or loaded most recently. 
        // Empty string lets vanilla handle everything.
        public static string levelToLoad = "";


        static Scene currentScene;
        static FistVR.TNH_Manager manager;
        static GameObject loadedRoot;
        /// <summary>
        /// Perform the full loading and setting up of a custom TNH scene.
        /// </summary>
        public static IEnumerator HandleTAH(Scene loaded)
        {
            // Handle TAH load if we're loading a non-vanilla scene.
            if (loaded.name == "TakeAndHoldClassic" && levelToLoad != "")
            {
                Reset();
                currentScene = SceneManager.GetActiveScene();
                LevelType type = LevelType.TNH;

                // Certain objects need to be interrupted before they can initialize, otherwise everything breaks.
                // Once everything has been overwritten, we re-enable these.
                SetWhitelistedStates(false, type);

                // There are a handful of vars that are pretty difficult to get our hands on.
                // So let's just steal them off existing objects before we delete them.
                CollectRequiredTNHObjects();

                // Destroy everything we no longer need.
                CleanByBlacklist(type);

                // Time to merge in the new scene.
                Entrypoint.self.StartCoroutine(MergeInScene());

                // Wait a few frames to make sure everything is peachy.
                yield return null;
                yield return null;
                yield return null;
                yield return null;

                // Resolve scene proxies to real TNH objects.
                ResolveAll(type);

                // Everything is set up, re-enable everything.
                SetWhitelistedStates(true, type);
            }
        }

        public static IEnumerator HandleGeneric(Scene loaded)
        {
            if (loaded.name == "ProvingGround" && levelToLoad != "")
            {
                Reset();
                currentScene = SceneManager.GetActiveScene();
                LevelType type = LevelType.Generic;

                SetWhitelistedStates(false, type);
                CleanByBlacklist(type);
                Entrypoint.self.StartCoroutine(MergeInScene());

                yield return null;
                yield return null;
                yield return null;
                yield return null;

                ResolveAll(type);
                SetWhitelistedStates(true, type);
            }
        }

        private static void Reset()
        {
            originalStates.Clear();
        }

        static Dictionary<string, bool> originalStates = new Dictionary<string, bool>();
        /// <summary>
        /// When state == false, Prevent whitelisted objects from initializing by quickly disabling them.
        /// When state == true, whitelisted objects will be set back to their original state.
        /// </summary>
        private static void SetWhitelistedStates(bool state, LevelType type)
        {
            // Decide which list to use
            List<string> whitelist = null;
            switch (type)
            {
                case LevelType.TNH:
                    whitelist = whitelistedObjectsTNH;
                    break;
                case LevelType.Generic:
                    whitelist = whitelistedObjectsGeneric;
                    break;
            }


            List<GameObject> whitelisted = currentScene.GetAllGameObjectsInScene().Where(x => whitelist.Contains(x.name)).ToList();

            // If we're running this for the first time, record initial state.
            // We do this because some whitelisted objects are disabled already.
            if (originalStates.Count == 0)
            {
                whitelisted.ForEach(x => originalStates[x.name] = x.activeInHierarchy);
            }

            foreach (GameObject ii in whitelisted)
            {
                if (originalStates.ContainsKey(ii.name) && originalStates[ii.name] == false) continue;
                else ii.SetActive(state);
            }
        }

        static FistVR.AudioEvent wave;
        static FistVR.AudioEvent success;
        static FistVR.AudioEvent failure;
        static GameObject vfx;
        static GameObject[] barrierPrefabs = new GameObject[2];
        /// <summary>
        /// A wide variety of existing objects are needed for importing a new TNH scene.
        /// This function grabs all of them.
        /// </summary>
        private static void CollectRequiredTNHObjects()
        {
            // We need HoldPoint AudioEvents.
            FistVR.TNH_HoldPoint sourceHoldPoint = currentScene.GetRootGameObjects().Where(x => x.name == "HoldPoint_0").First().GetComponent<FistVR.TNH_HoldPoint>();

            wave = new FistVR.AudioEvent();
            wave.Clips = sourceHoldPoint.AUDEvent_HoldWave.Clips.ToList();
            wave.VolumeRange = new Vector2(0.4f, 0.4f);
            wave.PitchRange = new Vector2(1, 1);
            wave.ClipLengthRange = new Vector2(1, 1);

            success = new FistVR.AudioEvent();
            success.Clips = sourceHoldPoint.AUDEvent_Success.Clips.ToList();
            success.VolumeRange = new Vector2(0.4f, 0.4f);
            success.PitchRange = new Vector2(1, 1);
            success.ClipLengthRange = new Vector2(1, 1);

            failure = new FistVR.AudioEvent();
            failure.Clips = sourceHoldPoint.AUDEvent_Failure.Clips.ToList();
            failure.VolumeRange = new Vector2(0.4f, 0.4f);
            failure.PitchRange = new Vector2(1, 1);
            failure.ClipLengthRange = new Vector2(1, 1);

            // We need VFX_HoldWave prefab.
            vfx = sourceHoldPoint.VFX_HoldWave;

            // We need barrier prefabs.
            FistVR.TNH_DestructibleBarrierPoint barrier = currentScene.GetAllGameObjectsInScene().Where(x => x.name == "Barrier_SpawnPoint").First().GetComponent<FistVR.TNH_DestructibleBarrierPoint>();
            barrierPrefabs[0] = barrier.BarrierDataSets[0].BarrierPrefab;
            barrierPrefabs[1] = barrier.BarrierDataSets[1].BarrierPrefab;
        }

        /// <summary>
        /// Nukes most objects in the TNH scene by blacklist.
        /// </summary>
        private static void CleanByBlacklist(LevelType type)
        {
            // Decide which blacklist to use.
            List<string> blacklist = null;
            switch (type)
            {
                case LevelType.TNH:
                    blacklist = blacklistedObjectsTNH;
                    break;
                case LevelType.Generic:
                    blacklist = blacklistedObjectsGeneric;
                    break;
            }

            // It just so happens the whole blacklist exists on the root. 
            // This will break if we need blacklist a non-root object!
            foreach (GameObject ii in currentScene.GetRootGameObjects())
            {
                if (blacklist.Contains(ii.name)) GameObject.Destroy(ii);
            }
        }

        static Dictionary<string, AssetBundle> loadedBundles = new Dictionary<string, AssetBundle>();
        /// <summary>
        /// Merges the custom level scene into the TNH scene.
        /// </summary>
        private static IEnumerator MergeInScene()
        {
            // First, make sure the bundle in question isn't already loaded.
            AssetBundle bundle;
            if (loadedBundles.ContainsKey(levelToLoad))
            {
                bundle = loadedBundles[levelToLoad];
            }
            else
            {
                bundle = AssetBundle.LoadFromFile(levelToLoad);
                loadedBundles[levelToLoad] = bundle;
            }

            // Get the scene from the bundle and load it.
            // Things have to happen in a pretty specific order and I got tired of
            // fighting with async so this will have to be a synchronous load.
            string scenePath = bundle.GetAllScenePaths()[0];
            SceneManager.LoadScene(Path.GetFileNameWithoutExtension(scenePath), LoadSceneMode.Additive);

            // Need to wait an extra frame for the scene to actually be active.
            yield return null;

            // Merge this newly loaded scene. 
            //! It *should* always be at the final index, but this might be imperfect.
            // Merge must happen in this direction. Otherwise, restart scene will break (among other things.)
            SceneManager.MergeScenes(SceneManager.GetSceneAt(SceneManager.sceneCount - 1), SceneManager.GetActiveScene());

            // Grab a few objects we'll need later.
            loadedRoot = currentScene.GetRootGameObjects().Single(x => x.name == "[TNHLEVEL]" || x.name == "[LEVEL]");

            GameObject managerObj = currentScene.GetRootGameObjects().Where(x => x.name == "_GameManager").FirstOrDefault();
            if (managerObj != null)
            {
                manager = managerObj.GetComponent<FistVR.TNH_Manager>();
            }
        }

        /// <summary>
        /// Resolve all proxies into valid TNH components.
        /// </summary>
        private static void ResolveAll(LevelType type)
        {
            Resolve_Skybox();
            Resolve_Shaders();
            Resolve_Terrain();
            Resolve_PMats();
            Resolve_FVRReverbEnvironments();
            Resolve_FVRHandGrabPoints();
            Resolve_AICoverPoints();
            Resolve_Targets();
            if (type == LevelType.TNH) Resolve_TNH_DestructibleBarrierPoints();
            if (type == LevelType.TNH) Resolve_TNH_SupplyPoints();
            if (type == LevelType.TNH) Resolve_TNH_HoldPoints();
            if (type == LevelType.TNH) Resolve_ScoreboardArea();
            if (type == LevelType.Generic) Resolve_Spawn();
            if (type == LevelType.Generic) Resolve_ItemSpawners();
            if (type == LevelType.Generic) Resolve_GenericPrefabs();


            if (type == LevelType.TNH) Fix_TNH_Manager();
        }

        #region Resolves
        /// <summary>
        /// Use the skybox of the imported level.
        /// Requires GI Update to fix lighting.
        /// </summary>
        private static void Resolve_Skybox()
        {
            TNH.TNH_Level levelComponent = loadedRoot.GetComponent<TNH.TNH_Level>();
            if (levelComponent.skybox != null)
            {
                RenderSettings.skybox = levelComponent.skybox;
                RenderSettings.skybox.RefreshShader();
                DynamicGI.UpdateEnvironment();
            }
        }


        /// <summary>
        /// Shaders, when imported from an assetbundle, become garbage.
        /// Set them to themselves and bam, it works.
        /// Unity 5 bugs sure were something.
        /// </summary>
        private static void Resolve_Shaders()
        {
            foreach (MeshRenderer ii in loadedRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                foreach (Material jj in ii.materials)
                {
                    jj.RefreshShader();
                }
            }
        }

        private static void Resolve_Terrain()
        {
            foreach (Terrain ii in loadedRoot.GetComponentsInChildren<Terrain>(true))
            {
                ii.materialTemplate.RefreshShader();
                ii.terrainData.treePrototypes.Select(x => x.prefab.layer = LayerMask.NameToLayer("Environment"));
                foreach (TreePrototype jj in ii.terrainData.treePrototypes)
                {
                    jj.prefab.layer = LayerMask.NameToLayer("Environment");
                    MeshRenderer[] mrs = jj.prefab.GetComponentsInChildren<MeshRenderer>();
                    mrs.ForEach(x => x.material.RefreshShader());
                }
                foreach (TreeInstance jj in ii.terrainData.treeInstances)
                {
                    GameObject copiedTree = GameObject.Instantiate<GameObject>(ii.terrainData.treePrototypes[jj.prototypeIndex].prefab, ii.transform);
                    copiedTree.transform.localPosition = new Vector3(ii.terrainData.size.x * jj.position.x, ii.terrainData.size.y * jj.position.y, ii.terrainData.size.z * jj.position.z);
                    copiedTree.transform.localScale = new Vector3(jj.widthScale, jj.heightScale, jj.widthScale);
                    copiedTree.transform.localEulerAngles = new Vector3(0f, jj.rotation, 0f);
                }
                ii.terrainData.treeInstances = new TreeInstance[0];
            }
        }

        /// <summary>
        /// Creates valid PMats from proxies.
        /// </summary>
        private static void Resolve_PMats()
        {
            WurstMod.TNH.PMat[] pMatProxies = loadedRoot.GetComponentsInChildren<WurstMod.TNH.PMat>(true);
            foreach (var proxy in pMatProxies)
            {
                GameObject owner = proxy.gameObject;
                FistVR.PMat real = owner.AddComponent<FistVR.PMat>();

                if (proxy.def == WurstMod.TNH.PMat.Def.None) real.Def = null;
                else real.Def = Resources.Load<FistVR.PMaterialDefinition>("pmaterialdefinitions/" + proxy.GetDef((int)proxy.def));

                real.MatDef = Resources.Load<FistVR.MatDef>("matdefs/" + proxy.GetMatDef((int)proxy.matDef));
            }
        }

        /// <summary>
        /// Creates valid VRReverbEnvironments from proxies.
        /// </summary>
        private static void Resolve_FVRReverbEnvironments()
        {
            WurstMod.TNH.FVRReverbEnvironment[] reverbProxies = loadedRoot.GetComponentsInChildren<WurstMod.TNH.FVRReverbEnvironment>(true);
            foreach (var proxy in reverbProxies)
            {
                GameObject owner = proxy.gameObject;
                FistVR.FVRReverbEnvironment real = owner.AddComponent<FistVR.FVRReverbEnvironment>();

                real.Environment = (FistVR.FVRSoundEnvironment)proxy.Environment;
                real.Priority = proxy.Priority;
            }
        }

        /// <summary>
        /// Creates valid FVRHandGrabPoints from proxies.
        /// </summary>
        private static void Resolve_FVRHandGrabPoints()
        {
            TNH.FVRHandGrabPoint[] grabProxies = loadedRoot.GetComponentsInChildren<TNH.FVRHandGrabPoint>(true);
            foreach (var proxy in grabProxies)
            {
                GameObject owner = proxy.gameObject;
                FistVR.FVRHandGrabPoint real = owner.AddComponent<FistVR.FVRHandGrabPoint>();

                real.UXGeo_Hover = proxy.UXGeo_Hover;
                real.UXGeo_Held = proxy.UXGeo_Held;
                real.PositionInterpSpeed = 1;
                real.RotationInterpSpeed = 1;

                // Messy math for interaction distance.
                Collider proxyCol = proxy.GetComponent<Collider>();
                Vector3 extents = proxyCol.bounds.extents;
                real.EndInteractionDistance = 2.5f * Mathf.Abs(Mathf.Max(extents.x, extents.y, extents.z));
            }
        }

        /// <summary>
        /// Creates valid AICoverPoints from proxies.
        /// </summary>
        private static void Resolve_AICoverPoints()
        {
            // NOTE: AICoverPoint currently isn't in the FistVR namespace.
            TNH.AICoverPoint[] coverProxies = loadedRoot.GetComponentsInChildren<TNH.AICoverPoint>(true);
            foreach (var proxy in coverProxies)
            {
                GameObject owner = proxy.gameObject;
                AICoverPoint real = owner.AddComponent<AICoverPoint>();

                // These seem to be constant, and Calc and CalcNew are an enigma.
                real.Heights = new float[] { 3f, 0.5f, 1.1f, 1.5f };
                real.Calc();
                real.CalcNew();
            }
        }

        /// <summary>
        /// Creates valid TNH_DestructibleBarrierPoints from proxies.
        /// </summary>
        private static void Resolve_TNH_DestructibleBarrierPoints()
        {
            TNH.TNH_DestructibleBarrierPoint[] coverProxies = loadedRoot.GetComponentsInChildren<TNH.TNH_DestructibleBarrierPoint>(true);
            foreach (var proxy in coverProxies)
            {
                GameObject owner = proxy.gameObject;
                FistVR.TNH_DestructibleBarrierPoint real = owner.AddComponent<FistVR.TNH_DestructibleBarrierPoint>();

                real.Obstacle = owner.GetComponent<UnityEngine.AI.NavMeshObstacle>();
                real.CoverPoints = real.GetComponentsInChildren<AICoverPoint>(true).ToList();
                real.BarrierDataSets = new List<FistVR.TNH_DestructibleBarrierPoint.BarrierDataSet>();

                for (int ii = 0; ii < 2; ii++)
                {
                    FistVR.TNH_DestructibleBarrierPoint.BarrierDataSet barrierSet = new FistVR.TNH_DestructibleBarrierPoint.BarrierDataSet();
                    barrierSet.BarrierPrefab = barrierPrefabs[ii];
                    barrierSet.Points = new List<FistVR.TNH_DestructibleBarrierPoint.BarrierDataSet.SavedCoverPointData>();

                    real.BarrierDataSets.Add(barrierSet);
                }

                // This should only be run in the editor, but OH WELL.
                real.BakePoints();
            }
        }

        /// <summary>
        /// Creates valid TNH_SupplyPoints from proxies.
        /// </summary>
        private static void Resolve_TNH_SupplyPoints()
        {
            TNH.TNH_SupplyPoint[] supplyProxies = loadedRoot.GetComponentsInChildren<TNH.TNH_SupplyPoint>(true);
            foreach (var proxy in supplyProxies)
            {
                GameObject owner = proxy.gameObject;
                FistVR.TNH_SupplyPoint real = owner.AddComponent<FistVR.TNH_SupplyPoint>();

                real.M = manager;
                real.Bounds = proxy.Bounds;
                real.CoverPoints = proxy.CoverPoints.AsEnumerable().Select(x => x.gameObject.GetComponent<AICoverPoint>()).ToList();
                real.SpawnPoint_PlayerSpawn = proxy.SpawnPoint_PlayerSpawn;
                real.SpawnPoints_Sosigs_Defense = proxy.SpawnPoints_Sosigs_Defense.AsEnumerable().ToList();
                real.SpawnPoints_Turrets = proxy.SpawnPoints_Turrets.AsEnumerable().ToList();
                real.SpawnPoints_Panels = proxy.SpawnPoints_Panels.AsEnumerable().ToList();
                real.SpawnPoints_Boxes = proxy.SpawnPoints_Boxes.AsEnumerable().ToList();
                real.SpawnPoint_Tables = proxy.SpawnPoint_Tables.AsEnumerable().ToList();
                real.SpawnPoint_CaseLarge = proxy.SpawnPoint_CaseLarge;
                real.SpawnPoint_CaseSmall = proxy.SpawnPoint_CaseSmall;
                real.SpawnPoint_Melee = proxy.SpawnPoint_Melee;
                real.SpawnPoints_SmallItem = proxy.SpawnPoints_SmallItem.AsEnumerable().ToList();
                real.SpawnPoint_Shield = proxy.SpawnPoint_Shield;
            }
        }

        /// <summary>
        /// Creates valid TNH_HoldPoints from proxies.
        /// </summary>
        private static void Resolve_TNH_HoldPoints()
        {
            TNH.TNH_HoldPoint[] holdProxies = loadedRoot.GetComponentsInChildren<TNH.TNH_HoldPoint>(true);
            foreach (var proxy in holdProxies)
            {
                GameObject owner = proxy.gameObject;
                FistVR.TNH_HoldPoint real = owner.AddComponent<FistVR.TNH_HoldPoint>();

                real.M = manager;
                real.Bounds = proxy.Bounds;
                real.NavBlockers = proxy.NavBlockers;
                real.BarrierPoints = proxy.BarrierPoints.AsEnumerable().Select(x => x.gameObject.GetComponent<FistVR.TNH_DestructibleBarrierPoint>()).ToList();
                real.CoverPoints = proxy.CoverPoints.AsEnumerable().Select(x => x.gameObject.GetComponent<AICoverPoint>()).ToList();
                real.SpawnPoint_SystemNode = proxy.SpawnPoint_SystemNode;
                real.SpawnPoints_Targets = proxy.SpawnPoints_Targets.AsEnumerable().ToList();
                real.SpawnPoints_Turrets = proxy.SpawnPoints_Turrets.AsEnumerable().ToList();
                real.AttackVectors = proxy.AttackVectors.AsEnumerable().Select(x => Resolve_AttackVector(x.GetComponent<TNH.AttackVector>())).ToList();
                real.SpawnPoints_Sosigs_Defense = proxy.SpawnPoints_Sosigs_Defense.AsEnumerable().ToList();

                real.AUDEvent_HoldWave = wave;
                real.AUDEvent_Success = success;
                real.AUDEvent_Failure = failure;
                real.VFX_HoldWave = vfx;
            }
        }

        /// <summary>
        /// Creates a valid H3VR AttackVector from a proxy.
        /// </summary>
        private static FistVR.TNH_HoldPoint.AttackVector Resolve_AttackVector(TNH.AttackVector proxy)
        {
            GameObject owner = proxy.gameObject;
            FistVR.TNH_HoldPoint.AttackVector real = new FistVR.TNH_HoldPoint.AttackVector();

            real.SpawnPoints_Sosigs_Attack = proxy.SpawnPoints_Sosigs_Attack;
            real.GrenadeVector = proxy.GrenadeVector;
            real.GrenadeRandAngle = proxy.GrenadeRandAngle;
            real.GrenadeVelRange = proxy.GrenadeVelRange;

            return real;
        }

        /// <summary>
        /// Places the H3VR scoreboard at the correct position.
        /// </summary>
        private static void Resolve_ScoreboardArea()
        {
            GameObject proxy = loadedRoot.GetComponentInChildren<TNH.ScoreboardArea>().gameObject;
            GameObject finalScore = currentScene.GetAllGameObjectsInScene().Where(x => x.name == "_FinalScore").First();
            GameObject resetPoint = currentScene.GetAllGameObjectsInScene().Where(x => x.name == "[ResetPoint]").First();

            resetPoint.transform.position = proxy.transform.position;
            finalScore.transform.position = proxy.transform.position + new Vector3(0f, 1.8f, 7.5f);

        }

        private static void Resolve_Spawn()
        {
            Transform spawnPoint = loadedRoot.GetComponentInChildren<Generic.Spawn>().transform;
            GameObject cameraRig = currentScene.GetAllGameObjectsInScene().Where(x => x.name == "[CameraRig]Fixed").First();
            cameraRig.transform.position = spawnPoint.position;
        }

        private static void Resolve_ItemSpawners()
        {
            Transform[] itemSpawners = loadedRoot.GetComponentsInChildren<Generic.ItemSpawner>().Select(x => x.transform).ToArray();
            GameObject spawnerBase = currentScene.GetAllGameObjectsInScene().Where(x => x.name == "ItemSpawner").First();
            foreach (Transform ii in itemSpawners)
            {
                GameObject spawner = GameObject.Instantiate(spawnerBase, loadedRoot.transform);
                //GameObject spawner = spawnerBase;
                spawner.transform.position = ii.position + (0.8f * Vector3.up);
                spawner.transform.localEulerAngles = ii.localEulerAngles;
                spawner.SetActive(true);

                Debug.Log(string.Join("\n", spawner.transform.AsEnumerable().Select(x => x.name + ", " + x.transform.position.ToString() ).ToArray()));
                Debug.Log(spawner.transform.position);
            }
        }

        private static Dictionary<Generic.Prefab, GameObject> baseObjects = new Dictionary<Generic.Prefab, GameObject>();
        private static void Resolve_GenericPrefabs()
        {
            // For populating list cleanly.
            void Add(Generic.Prefab type, string objName)
            {
                baseObjects[type] = currentScene.GetAllGameObjectsInScene().Where(x => x.name == objName).First();
                baseObjects[type].SetActive(false);
            }

            Transform[] prefabs = loadedRoot.GetComponentsInChildren<Generic.GenericPrefab>().Select(x => x.transform).ToArray();
            Add(Generic.Prefab.ItemSpawner, "ItemSpawner");
            Add(Generic.Prefab.Destructobin, "Destructobin");
            Add(Generic.Prefab.SosigSpawner, "SosigSpawner");
            Add(Generic.Prefab.WhizzBangADinger, "WhizzBangADinger2");
            Add(Generic.Prefab.WhizzBangADingerDetonator, "BangerDetonator");

            // Create objects based on type.
            Generic.GenericPrefab[] genericPrefabs = loadedRoot.GetComponentsInChildren<Generic.GenericPrefab>();
            foreach (Generic.GenericPrefab ii in genericPrefabs)
            {
                GameObject copy = GameObject.Instantiate(baseObjects[ii.objectType], loadedRoot.transform);
                copy.transform.position = ii.transform.position;
                copy.transform.localEulerAngles = ii.transform.localEulerAngles;
                copy.SetActive(true);
            }
        }

        private static void Resolve_Targets()
        {
            Target[] targets = loadedRoot.GetComponentsInChildren<Any.Target>().ToArray();
            foreach (Target ii in targets)
            {
                ReactiveSteelTarget baseTarget = ii.gameObject.AddComponent<ReactiveSteelTarget>();
                baseTarget.HitEvent = new AudioEvent();
                baseTarget.HitEvent.Clips = ii.clips;
                baseTarget.HitEvent.VolumeRange = ii.volumeRange;
                baseTarget.HitEvent.PitchRange = ii.pitchRange;
                baseTarget.HitEvent.ClipLengthRange = ii.speedRange;

                if (baseTarget.HitEvent.Clips.Count == 0)
                {
                    baseTarget.HitEvent.Clips = new List<AudioClip>();
                    baseTarget.HitEvent.Clips.Add(new AudioClip());
                }

                baseTarget.BulletHolePrefabs = new GameObject[0];
            }
        }


        /// <summary>
        /// Base function for setting up the TNH Manager object to handle a custom level.
        /// </summary>
        private static void Fix_TNH_Manager()
        {
            // Hold points need to be set.
            manager.HoldPoints = loadedRoot.GetComponentsInChildren<FistVR.TNH_HoldPoint>(true).ToList();

            // Supply points need to be set.
            manager.SupplyPoints = loadedRoot.GetComponentsInChildren<FistVR.TNH_SupplyPoint>(true).ToList();

            // Possible Sequences need to be generated at random.
            manager.PossibleSequnces = GenerateRandomPointSequences(10);

            // Safe Pos Matrix needs to be set. Diagonal for now.
            FistVR.TNH_SafePositionMatrix maxMatrix = GenerateTestMatrix();
            manager.SafePosMatrix = maxMatrix;
        }

        /// <summary>
        /// Regular gamemode uses a preset list of possible hold orders. This creates a bunch randomly.
        /// </summary>
        private static List<FistVR.TNH_PointSequence> GenerateRandomPointSequences(int count)
        {
            List<FistVR.TNH_PointSequence> sequences = new List<FistVR.TNH_PointSequence>();
            for (int ii = 0; ii < count; ii++)
            {
                FistVR.TNH_PointSequence sequence = ScriptableObject.CreateInstance<FistVR.TNH_PointSequence>();

                // Logic for forced spawn location.
                ForcedSpawn forcedSpawn = loadedRoot.GetComponentInChildren<ForcedSpawn>();
                if (forcedSpawn != null)
                {
                    sequence.StartSupplyPointIndex = manager.SupplyPoints.IndexOf(manager.SupplyPoints.First(x => x.gameObject == forcedSpawn.gameObject));
                }
                else
                {
                    sequence.StartSupplyPointIndex = UnityEngine.Random.Range(0, manager.SupplyPoints.Count);
                }

                sequence.HoldPoints = new List<int>()
                {
                    //TODO This should only be 5, but I got an outofrange after the 4th hold so let's just add more... Maybe that'll fix it...
                    UnityEngine.Random.Range(0, manager.HoldPoints.Count),
                    UnityEngine.Random.Range(0, manager.HoldPoints.Count),
                    UnityEngine.Random.Range(0, manager.HoldPoints.Count),
                    UnityEngine.Random.Range(0, manager.HoldPoints.Count),
                    UnityEngine.Random.Range(0, manager.HoldPoints.Count),
                    UnityEngine.Random.Range(0, manager.HoldPoints.Count),
                    UnityEngine.Random.Range(0, manager.HoldPoints.Count)
                };

                // Fix sequence, because they may generate the same point after the current point, IE {1,4,4)
                // This would break things.
                for(int jj = 0; jj < sequence.HoldPoints.Count - 1; jj++)
                {
                    if (sequence.HoldPoints[jj] == sequence.HoldPoints[jj + 1])
                    {
                        sequence.HoldPoints[jj + 1] = (sequence.HoldPoints[jj + 1] + 1) % manager.HoldPoints.Count;
                    }
                }

                sequences.Add(sequence);
            }
            return sequences;
        }

        /// <summary>
        /// Creates a matrix of valid Hold Points and Supply Points (I think) only used for endless?
        /// By default, just generates a matrix that is false on diagonals.
        /// </summary>
        private static FistVR.TNH_SafePositionMatrix GenerateTestMatrix()
        {
            FistVR.TNH_SafePositionMatrix maxMatrix = ScriptableObject.CreateInstance<FistVR.TNH_SafePositionMatrix>();
            maxMatrix.Entries_HoldPoints = new List<FistVR.TNH_SafePositionMatrix.PositionEntry>();
            maxMatrix.Entries_SupplyPoints = new List<FistVR.TNH_SafePositionMatrix.PositionEntry>();

            int effectiveHoldCount = manager.HoldPoints.Count;
            int effectiveSupplyCount = manager.SupplyPoints.Where(x => x.GetComponent<ForcedSpawn>() == null).Count();

            for (int ii = 0; ii < effectiveHoldCount; ii++)
            {
                FistVR.TNH_SafePositionMatrix.PositionEntry entry = new FistVR.TNH_SafePositionMatrix.PositionEntry();
                entry.SafePositions_HoldPoints = new List<bool>();
                for (int jj = 0; jj < effectiveHoldCount; jj++)
                {
                    entry.SafePositions_HoldPoints.Add(ii != jj);
                }

                entry.SafePositions_SupplyPoints = new List<bool>();
                for (int jj = 0; jj < effectiveSupplyCount; jj++)
                {
                    entry.SafePositions_SupplyPoints.Add(true);
                }

                maxMatrix.Entries_HoldPoints.Add(entry);
            }

            for (int ii = 0; ii < effectiveSupplyCount; ii++)
            {
                FistVR.TNH_SafePositionMatrix.PositionEntry entry = new FistVR.TNH_SafePositionMatrix.PositionEntry();
                entry.SafePositions_HoldPoints = new List<bool>();
                for (int jj = 0; jj < effectiveHoldCount; jj++)
                {
                    entry.SafePositions_HoldPoints.Add(true);
                }

                entry.SafePositions_SupplyPoints = new List<bool>();
                for (int jj = 0; jj < effectiveSupplyCount; jj++)
                {
                    entry.SafePositions_SupplyPoints.Add(ii != jj);
                }

                maxMatrix.Entries_SupplyPoints.Add(entry);
            }

            return maxMatrix;
        }
        #endregion
    }
}

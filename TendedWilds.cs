using MelonLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System;

[assembly: MelonInfo(typeof(TendedWilds.TendedWildsMod), "Tended Wilds", "1.0.5", "SageDragoon")]
[assembly: MelonGame("Crate Entertainment", "Farthest Frontier")]

namespace TendedWilds
{
    public class TendedWildsMod : MelonMod
    {
        private static readonly BindingFlags AllInstance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly BindingFlags AllStatic =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static bool techTreePatched = false;
        private static bool harvestedThisYear = false;
        private static int lastKnownYear = -1;
        private static GameManager cachedGameManager = null;

        // Priority system: per-shack, per-item priority values 1-9 (default 5)
        // Key = ForagerShack instance ID, Value = dict of ItemID -> priority (1-9)
        internal static Dictionary<int, Dictionary<int, int>> shackPriorities =
            new Dictionary<int, Dictionary<int, int>>();

        // (Uber system removed — vanilla toggle + SetPrioritized postfix handles it)

        // Maps priority 1-9 to score values for _scoreByForagingBucket
        internal static readonly int[] PriorityToScore = new int[]
        {
            0,    // index 0 unused
            0,    // 1 = lowest
            62,   // 2
            125,  // 3
            187,  // 4
            250,  // 5 = default
            312,  // 6
            375,  // 7
            437,  // 8
            500   // 9 = highest
        };

        internal const int DEFAULT_PRIORITY = 5;

        public override void OnInitializeMelon()
        {
            // --- Conflict detection ---
            foreach (var melon in MelonBase.RegisteredMelons)
            {
                if (melon == this) continue;
                string name = melon.Info?.Name ?? "";
                if (name.IndexOf("Forageable Transplantation", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("ForageableTransplantation", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    MelonLogger.BigError("CONFLICT DETECTED",
                        "Forageable Transplantation is loaded alongside Tended Wilds.\n" +
                        "Tended Wilds already includes all Forageable Transplantation functionality.\n" +
                        "Running both WILL cause duplicate Harmony patches and unpredictable behavior.\n" +
                        "Please remove ForageableTransplantation.dll from your Mods folder.");
                }
            }

            try
            {
                var harmony = new HarmonyLib.Harmony("com.sagedragoon.tendedwilds");

                Type foragerShackType = null;
                Type uiForagingProductivityType = null;
                Type uiForagingIconType = null;
                Type buildingType = null;
                Type inputPlaceBuildingType = null;
                Type uiSubwidgetForagerShackType = null;
                Type terrainObjectBuildsiteType = null;
                Type buildSiteType = null;
                Type buildManagerType = null;

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (foragerShackType == null) foragerShackType = asm.GetType("ForagerShack");
                    if (uiForagingProductivityType == null) uiForagingProductivityType = asm.GetType("UIForagingProductivitySubWidget");
                    if (uiForagingIconType == null) uiForagingIconType = asm.GetType("UIForagingIcon");
                    if (buildingType == null) buildingType = asm.GetType("Building");
                    if (inputPlaceBuildingType == null) inputPlaceBuildingType = asm.GetType("Input_PlaceBuilding");
                    if (uiSubwidgetForagerShackType == null) uiSubwidgetForagerShackType = asm.GetType("UISubwidgetForagerShack");
                    if (terrainObjectBuildsiteType == null) terrainObjectBuildsiteType = asm.GetType("TerrainObjectBuildsite");
                    if (buildSiteType == null) buildSiteType = asm.GetType("BuildSite");
                    if (buildManagerType == null) buildManagerType = asm.GetType("BuildManager");
                    if (foragerShackType != null && uiForagingProductivityType != null
                        && uiForagingIconType != null && buildingType != null
                        && inputPlaceBuildingType != null && uiSubwidgetForagerShackType != null
                        && terrainObjectBuildsiteType != null && buildSiteType != null
                        && buildManagerType != null)
                        break;
                }

                // --- Year-round cultivation ---
                if (foragerShackType != null)
                {
                    var growMethod = foragerShackType.GetMethod("GrowCultivatedItem",
                        AllInstance | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                    if (growMethod != null)
                    {
                        harmony.Patch(growMethod, prefix: new HarmonyMethod(
                            typeof(ForagerShackPatches).GetMethod(nameof(ForagerShackPatches.GrowCultivatedItemPrefix), AllStatic)));
                        MelonLogger.Msg("Patched ForagerShack.GrowCultivatedItem (year-round cultivation)");
                    }

                    var destroyMethod = foragerShackType.GetMethod("DestroyCultivatedItems",
                        AllInstance | BindingFlags.DeclaredOnly);
                    if (destroyMethod != null)
                    {
                        harmony.Patch(destroyMethod, prefix: new HarmonyMethod(
                            typeof(ForagerShackPatches).GetMethod(nameof(ForagerShackPatches.DestroyCultivatedItemsPrefix), AllStatic)));
                        MelonLogger.Msg("Patched ForagerShack.DestroyCultivatedItems (prevent winter destruction)");
                    }

                    // Issue 2: Patch OnCultivatedItemEmpty to trigger regrowth after harvest
                    var onEmptyMethod = foragerShackType.GetMethod("OnCultivatedItemEmpty",
                        AllInstance | BindingFlags.DeclaredOnly);
                    if (onEmptyMethod != null)
                    {
                        harmony.Patch(onEmptyMethod, postfix: new HarmonyMethod(
                            typeof(ForagerShackPatches).GetMethod(nameof(ForagerShackPatches.OnCultivatedItemEmptyPostfix), AllStatic)));
                        MelonLogger.Msg("Patched ForagerShack.OnCultivatedItemEmpty (harvest regrowth)");
                    }

                    // Issue 2: Patch UpdateScales to undo winter cancel (greenhouse effect)
                    var updateScalesMethod = foragerShackType.GetMethod("UpdateScales",
                        AllInstance | BindingFlags.DeclaredOnly);
                    if (updateScalesMethod != null)
                    {
                        harmony.Patch(updateScalesMethod, postfix: new HarmonyMethod(
                            typeof(ForagerShackPatches).GetMethod(nameof(ForagerShackPatches.UpdateScalesPostfix), AllStatic)));
                        MelonLogger.Msg("Patched ForagerShack.UpdateScales (undo winter cancel)");
                    }

                    // Issue 2: Patch OnSeasonChanged to undo CancelInvoke("UpdateScales") in winter
                    var onSeasonMethod = foragerShackType.GetMethod("OnSeasonChanged",
                        AllInstance | BindingFlags.DeclaredOnly);
                    if (onSeasonMethod != null)
                    {
                        harmony.Patch(onSeasonMethod, postfix: new HarmonyMethod(
                            typeof(ForagerShackPatches).GetMethod(nameof(ForagerShackPatches.OnSeasonChangedPostfix), AllStatic)));
                        MelonLogger.Msg("Patched ForagerShack.OnSeasonChanged (re-invoke UpdateScales)");
                    }
                }

                // --- Priority integration: patch SetPrioritized and IsPrioritized ---
                if (foragerShackType != null)
                {
                    var setPrioritized = foragerShackType.GetMethod("SetPrioritized",
                        AllInstance | BindingFlags.DeclaredOnly);
                    if (setPrioritized != null)
                    {
                        harmony.Patch(setPrioritized, postfix: new HarmonyMethod(
                            typeof(ForagerShackPatches).GetMethod(nameof(ForagerShackPatches.SetPrioritizedPostfix), AllStatic)));
                        MelonLogger.Msg("Patched ForagerShack.SetPrioritized (1-9 priority integration)");
                    }

                    var isPrioritized = foragerShackType.GetMethod("IsPrioritized",
                        AllInstance | BindingFlags.DeclaredOnly);
                    if (isPrioritized != null)
                    {
                        harmony.Patch(isPrioritized, postfix: new HarmonyMethod(
                            typeof(ForagerShackPatches).GetMethod(nameof(ForagerShackPatches.IsPrioritizedPostfix), AllStatic)));
                        MelonLogger.Msg("Patched ForagerShack.IsPrioritized (always true for toggles)");
                    }
                }

                // --- Feature 2: Ensure priority UI stays visible at tier 2+ ---
                // UIForagingProductivitySubWidget.Init already shows for all tiers (active = foragerShack != null)
                // but we patch it just in case future game updates add a tier check
                if (uiForagingProductivityType != null)
                {
                    var initMethod = uiForagingProductivityType.GetMethod("Init",
                        AllInstance | BindingFlags.DeclaredOnly);
                    if (initMethod != null)
                    {
                        harmony.Patch(initMethod, postfix: new HarmonyMethod(
                            typeof(PriorityUIPatches).GetMethod(nameof(PriorityUIPatches.ProductivityInitPostfix), AllStatic)));
                        MelonLogger.Msg("Patched UIForagingProductivitySubWidget.Init (ensure priority always visible)");
                    }
                }

                // Feature 3 (1-9 priority) is now applied inside ProductivityInitPostfix
                // after icons are positioned in the UI hierarchy

                // --- Feature 4: Rename to Forager's Greenhouse at tier 2 ---
                if (buildingType != null)
                {
                    var setBDRN = buildingType.GetMethod("SetBuildingDataRecordName",
                        AllInstance | BindingFlags.DeclaredOnly);
                    if (setBDRN != null)
                    {
                        harmony.Patch(setBDRN, postfix: new HarmonyMethod(
                            typeof(DisplayNamePatches).GetMethod(nameof(DisplayNamePatches.SetBuildingDataRecordNamePostfix), AllStatic)));
                        MelonLogger.Msg("Patched Building.SetBuildingDataRecordName (Greenhouse rename)");
                    }
                }

                // --- Issue 8: Patch SetCultivatedItem to refresh cost label dynamically ---
                if (foragerShackType != null)
                {
                    var setCultivated = foragerShackType.GetMethod("SetCultivatedItem",
                        AllInstance | BindingFlags.DeclaredOnly);
                    if (setCultivated != null)
                    {
                        harmony.Patch(setCultivated, postfix: new HarmonyMethod(
                            typeof(WildPlantingPatches).GetMethod(nameof(WildPlantingPatches.SetCultivatedItemPostfix), AllStatic)));
                        MelonLogger.Msg("Patched ForagerShack.SetCultivatedItem (cost label refresh)");
                    }
                }

                // --- Feature 5: Wild Planting ---
                // Patch UISubwidgetForagerShack.Init to add Plant button at tier 2
                if (uiSubwidgetForagerShackType != null)
                {
                    var initMethod = uiSubwidgetForagerShackType.GetMethod("Init",
                        AllInstance | BindingFlags.DeclaredOnly);
                    if (initMethod != null)
                    {
                        harmony.Patch(initMethod, postfix: new HarmonyMethod(
                            typeof(WildPlantingPatches).GetMethod(nameof(WildPlantingPatches.CultivationInitPostfix), AllStatic)));
                        MelonLogger.Msg("Patched UISubwidgetForagerShack.Init (Plant button)");
                    }
                }

                // Patch Input_PlaceBuilding.Construct to intercept wild planting ConstructionData
                if (inputPlaceBuildingType != null)
                {
                    var constructMethod = inputPlaceBuildingType.GetMethod("Construct",
                        AllInstance | BindingFlags.DeclaredOnly);
                    if (constructMethod != null)
                    {
                        harmony.Patch(constructMethod, prefix: new HarmonyMethod(
                            typeof(WildPlantingPatches).GetMethod(nameof(WildPlantingPatches.ConstructPrefix), AllStatic)));
                        MelonLogger.Msg("Patched Input_PlaceBuilding.Construct (wild planting intercept)");
                    }
                }

                // Patch TerrainObjectBuildsite.OnBuiltPrefabInstantiated for wild planting spawn
                if (terrainObjectBuildsiteType != null)
                {
                    var onBuilt = terrainObjectBuildsiteType.GetMethod("OnBuiltPrefabInstantiated",
                        AllInstance | BindingFlags.DeclaredOnly);
                    if (onBuilt != null)
                    {
                        harmony.Patch(onBuilt, postfix: new HarmonyMethod(
                            typeof(WildPlantingPatches).GetMethod(nameof(WildPlantingPatches.OnBuiltPrefabInstantiatedPostfix), AllStatic)));
                        MelonLogger.Msg("Patched TerrainObjectBuildsite.OnBuiltPrefabInstantiated (wild planting spawn)");
                    }
                    else
                    {
                        // Fallback to BuildSite base
                        var onBuiltBase = buildSiteType?.GetMethod("OnBuiltPrefabInstantiated",
                            AllInstance);
                        if (onBuiltBase != null)
                        {
                            harmony.Patch(onBuiltBase, postfix: new HarmonyMethod(
                                typeof(WildPlantingPatches).GetMethod(nameof(WildPlantingPatches.OnBuiltPrefabInstantiatedPostfix), AllStatic)));
                            MelonLogger.Msg("Patched BuildSite.OnBuiltPrefabInstantiated (wild planting spawn, fallback)");
                        }
                    }
                }

                // --- Issue 3: Null-safe patch on UIHarvestableResourceWindow.Relocate ---
                Type uiHarvestWindowType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    uiHarvestWindowType = asm.GetType("UIHarvestableResourceWindow");
                    if (uiHarvestWindowType != null) break;
                }
                if (uiHarvestWindowType != null)
                {
                    var relocateUI = uiHarvestWindowType.GetMethod("Relocate",
                        AllInstance | BindingFlags.DeclaredOnly);
                    if (relocateUI != null)
                    {
                        harmony.Patch(relocateUI, prefix: new HarmonyMethod(
                            typeof(RelocationPatches).GetMethod(nameof(RelocationPatches.UIRelocatePrefix), AllStatic)));
                        MelonLogger.Msg("Patched UIHarvestableResourceWindow.Relocate (null-safe relocate)");
                    }
                }

                // --- Fix 8: Relocation patches (absorbed from Forageable Transplantation) ---
                if (buildManagerType != null)
                {
                    var relocate = buildManagerType.GetMethod("Relocate",
                        AllInstance);
                    if (relocate != null)
                    {
                        harmony.Patch(relocate, prefix: new HarmonyMethod(
                            typeof(RelocationPatches).GetMethod(nameof(RelocationPatches.RelocatePrefix), AllStatic)));
                        MelonLogger.Msg("Patched BuildManager.Relocate (relocation capture)");
                    }
                }

                if (buildSiteType != null)
                {
                    var bsInit = buildSiteType.GetMethod("Initialize", AllInstance);
                    if (bsInit != null)
                    {
                        harmony.Patch(bsInit, postfix: new HarmonyMethod(
                            typeof(RelocationPatches).GetMethod(nameof(RelocationPatches.BuildSiteInitializePostfix), AllStatic)));
                        MelonLogger.Msg("Patched BuildSite.Initialize (relocation link)");
                    }
                }

                // OnBuiltPrefabInstantiated for relocation spawn — reuse the TerrainObjectBuildsite patch
                // (WildPlantingPatches.OnBuiltPrefabInstantiatedPostfix already handles wild planting;
                //  we add a separate postfix for relocation)
                if (terrainObjectBuildsiteType != null)
                {
                    var onBuilt = terrainObjectBuildsiteType.GetMethod("OnBuiltPrefabInstantiated",
                        AllInstance | BindingFlags.DeclaredOnly);
                    if (onBuilt != null)
                    {
                        harmony.Patch(onBuilt, postfix: new HarmonyMethod(
                            typeof(RelocationPatches).GetMethod(nameof(RelocationPatches.OnBuiltPrefabInstantiatedPostfix), AllStatic)));
                        MelonLogger.Msg("Patched TerrainObjectBuildsite.OnBuiltPrefabInstantiated (relocation spawn)");
                    }
                    else
                    {
                        var onBuiltBase = buildSiteType?.GetMethod("OnBuiltPrefabInstantiated", AllInstance);
                        if (onBuiltBase != null)
                        {
                            harmony.Patch(onBuiltBase, postfix: new HarmonyMethod(
                                typeof(RelocationPatches).GetMethod(nameof(RelocationPatches.OnBuiltPrefabInstantiatedPostfix), AllStatic)));
                            MelonLogger.Msg("Patched BuildSite.OnBuiltPrefabInstantiated (relocation spawn, fallback)");
                        }
                    }
                }

                MelonLogger.Msg("Tended Wilds v1.0.0: Harmony patches applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"OnInitializeMelon error: {ex}");
            }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (sceneName != "Map") return;

            MelonLogger.Msg($"OnSceneWasLoaded: sceneName='{sceneName}', buildIndex={buildIndex}");
            techTreePatched = false;
            harvestedThisYear = false;
            lastKnownYear = -1;
            cachedGameManager = null;
            shackPriorities.Clear();
            WildPlantingPatches.Reset();
            MelonCoroutines.Start(PatchTechTreeDelayed());
            MelonCoroutines.Start(AutoHarvestWatcher());
            MelonCoroutines.Start(WildPlantingPatches.ScoutBlueberryIdentifier());
            MelonCoroutines.Start(ApplyBuildingData());
            // Save reload safety net: re-run ApplyBuildingData at longer intervals
            // to handle cases where save deserialization takes longer than our
            // initial 10s + 10x5s retry window. Idempotent — only processes
            // forageables with null _buildingData, so already-set ones are skipped.
            MelonCoroutines.Start(ApplyBuildingDataDelayedPass(30f));
            MelonCoroutines.Start(ApplyBuildingDataDelayedPass(90f));
            // YearChangeWatcher removed — relocated/planted forageables inherit _buildingData from prefab
            RelocationPatches.PendingRelocations.Clear();
        }

        private IEnumerator ApplyBuildingDataDelayedPass(float delay)
        {
            yield return new WaitForSeconds(delay);
            MelonLogger.Msg($"ApplyBuildingData: Running safety-net pass after {delay}s delay (catches late-loaded saves).");
            MelonCoroutines.Start(ApplyBuildingData());
        }

        // =====================================================================
        // Woodlore tech tree modification
        // =====================================================================
        private IEnumerator PatchTechTreeDelayed()
        {
            yield return new WaitForSeconds(5f);

            // Patient retry — up to 30 minutes. Handles slow settlement creation
            // with map preview mods where the game can sit on the config screen
            // for 5-10+ minutes before the tech tree is initialized.
            // Checks every 3 seconds; stops retrying once patched or after 30min.
            int attempts = 0;
            const int maxAttempts = 600;  // 600 × 3s = 30 minutes
            while (!techTreePatched && attempts < maxAttempts)
            {
                attempts++;
                bool shouldRetry = !TryPatchTechTree(attempts);
                if (!shouldRetry) break;
                yield return new WaitForSeconds(3f);
            }

            if (!techTreePatched)
                MelonLogger.Error("PatchTechTree: Failed to patch Woodlore after 30 minutes of retrying. Consider reporting a bug.");
        }

        private bool TryPatchTechTree(int attempt)
        {
            try
            {
                var gameManager = GameObject.FindObjectOfType<GameManager>();
                if (gameManager == null) return false;

                var techTreeManager = gameManager.techTreeManager;
                if (techTreeManager == null) return false;

                var nodeDataList = techTreeManager.techTreeNodeData;
                if (nodeDataList == null || nodeDataList.Count == 0)
                {
                    MelonLogger.Warning($"PatchTechTree: techTreeNodeData empty (attempt {attempt}/20)");
                    return false;
                }

                TechTreeNodeData woodloreNode = null;
                foreach (var node in nodeDataList)
                {
                    if (node.GetTechName() == "Woodlore")
                    {
                        woodloreNode = node;
                        break;
                    }
                }

                if (woodloreNode == null)
                {
                    MelonLogger.Warning($"PatchTechTree: 'Woodlore' node not found (attempt {attempt}/20)");
                    return false;
                }

                var numRanksField = typeof(TechTreeNodeData).GetField("numRanks", AllInstance);
                if (numRanksField != null)
                {
                    int oldRanks = woodloreNode.GetNumRanks();
                    numRanksField.SetValue(woodloreNode, 2);
                    MelonLogger.Msg($"PatchTechTree: Woodlore numRanks {oldRanks} -> 2");
                }

                if (woodloreNode.gameEffectsEntries != null)
                {
                    var valueField = typeof(GameEffectEntry).GetField("_value", AllInstance);
                    foreach (var entry in woodloreNode.gameEffectsEntries)
                    {
                        if (entry.gameEffect != null &&
                            entry.gameEffect.GetType().Name == "GE_IncreaseForageableResourceYield")
                        {
                            if (valueField != null)
                            {
                                float oldVal = entry.value;
                                valueField.SetValue(entry, 3f);
                                MelonLogger.Msg($"PatchTechTree: Woodlore yield value {oldVal} -> 3");
                            }
                            break;
                        }
                    }
                }

                techTreePatched = true;
                MelonLogger.Msg("PatchTechTree: Woodlore modifications complete.");
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"PatchTechTree error (attempt {attempt}): {ex}");
                return false;
            }
        }

        // =====================================================================
        // Auto-harvest cultivated items at day 355
        // =====================================================================
        private IEnumerator AutoHarvestWatcher()
        {
            yield return new WaitForSeconds(15f);
            MelonLogger.Msg("AutoHarvestWatcher: Started.");

            while (true)
            {
                yield return new WaitForSeconds(5f);
                CheckAutoHarvest();
            }
        }

        private void CheckAutoHarvest()
        {
            try
            {
                if (cachedGameManager == null)
                {
                    cachedGameManager = GameObject.FindObjectOfType<GameManager>();
                    if (cachedGameManager == null) return;
                }

                var tm = cachedGameManager.timeManager;
                if (tm == null) return;

                var dateObj = tm.GetType()
                    .GetProperty("currentDate", AllInstance)
                    ?.GetValue(tm);
                if (dateObj == null) return;

                var dateType = dateObj.GetType();

                int currentYear = -1;
                var yearProp = dateType.GetProperty("year", AllInstance);
                var yearField = dateType.GetField("year", AllInstance);
                if (yearProp != null) currentYear = (int)yearProp.GetValue(dateObj);
                else if (yearField != null) currentYear = (int)yearField.GetValue(dateObj);

                int currentDayOfYear = -1;
                var dayProp = dateType.GetProperty("dayOfYear", AllInstance);
                var dayField = dateType.GetField("dayOfYear", AllInstance);
                if (dayProp != null) currentDayOfYear = (int)dayProp.GetValue(dateObj);
                else if (dayField != null) currentDayOfYear = (int)dayField.GetValue(dateObj);

                if (currentYear == -1 || currentDayOfYear == -1) return;

                if (currentYear != lastKnownYear && lastKnownYear != -1)
                {
                    harvestedThisYear = false;
                }
                lastKnownYear = currentYear;

                if (currentDayOfYear >= 355 && !harvestedThisYear)
                {
                    harvestedThisYear = true;
                    MelonLogger.Msg($"AutoHarvestWatcher: Day {currentDayOfYear} >= 355, triggering auto-harvest.");
                    PerformAutoHarvest();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"AutoHarvestWatcher error: {ex.Message}");
            }
        }

        private void PerformAutoHarvest()
        {
            try
            {
                var rm = cachedGameManager.resourceManager;
                if (rm == null) return;

                var shacksRO = rm.foragerShacksRO;
                if (shacksRO == null || shacksRO.Count == 0) return;

                var cultivatedItemsField = typeof(ForagerShack).GetField("cultivatedItems", AllInstance);
                var itemStorageField = typeof(ReservableItemStorage).GetField("itemStorage", AllInstance);
                var getCopyMethod = typeof(ItemStorage).GetMethod("GetCopyOfAllItems", AllInstance);

                if (cultivatedItemsField == null || itemStorageField == null || getCopyMethod == null)
                {
                    MelonLogger.Warning("AutoHarvest: Could not resolve reflection targets.");
                    return;
                }

                int totalTransferred = 0;

                foreach (ForagerShack shack in shacksRO)
                {
                    if (shack == null) continue;

                    ReservableItemStorage shackStorage = shack.storage;
                    if (shackStorage == null) continue;

                    var cultivatedList = cultivatedItemsField.GetValue(shack) as System.Collections.IList;
                    if (cultivatedList == null || cultivatedList.Count == 0) continue;

                    foreach (var cultivatedObj in cultivatedList)
                    {
                        if (cultivatedObj == null) continue;

                        var resourceField = cultivatedObj.GetType().GetField("resource", AllInstance);
                        if (resourceField == null) continue;

                        var resource = resourceField.GetValue(cultivatedObj) as ForageableResource;
                        if (resource == null) continue;

                        ReservableItemStorage resourceStorage = resource.storage;
                        if (resourceStorage == null) continue;

                        var innerStorage = itemStorageField.GetValue(resourceStorage) as ItemStorage;
                        if (innerStorage == null) continue;

                        var bundles = getCopyMethod.Invoke(innerStorage, null) as List<ItemBundle>;
                        if (bundles == null || bundles.Count == 0) continue;

                        foreach (var bundle in bundles)
                        {
                            if (bundle == null || bundle.numberOfItems == 0) continue;

                            uint count = bundle.numberOfItems;
                            shackStorage.AddItems(new ItemBundle(bundle));

                            bool reservationsModified;
                            resourceStorage.ForceRemoveItems(bundle, count, shack.gameObject, out reservationsModified);

                            totalTransferred += (int)count;
                            MelonLogger.Msg($"  AutoHarvest: {count}x {bundle.name} -> {shack.name}");
                        }
                    }
                }

                MelonLogger.Msg($"AutoHarvest: Complete. {totalTransferred} items transferred.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"AutoHarvest error: {ex}");
            }
        }

        // =====================================================================
        // Forageable Transplantation: enable relocation for all forageables
        // =====================================================================
        private IEnumerator ApplyBuildingData()
        {
            yield return new WaitForSeconds(10f);

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

            // Get the Bush_Blueberry_Small BuildingData directly from GlobalAssets.
            // This is the canonical serialized asset — available regardless of whether
            // blueberries have spawned on the map yet. Eliminates the need to scan
            // scene GameObjects and wait for forageables to populate.
            //
            // We still retry in case GlobalAssets isn't initialized yet (very early
            // scene load), but this resolves in 1-2 seconds on normal loads vs 60+s
            // of scene scanning on slow map configs.
            object templateBD = null;
            Type buildingDataType = null;

            int attempts = 0;
            const int maxAttempts = 60;  // 60 × 2s = 2 minutes — plenty for GlobalAssets init
            while (attempts < maxAttempts)
            {
                attempts++;
                try
                {
                    var bd = GlobalAssets.buildingSetupData?.GetBuildingData("Bush_Blueberry_Small");
                    if (bd != null)
                    {
                        templateBD = bd;
                        buildingDataType = bd.GetType();
                        MelonLogger.Msg($"ApplyBuildingData: Loaded 'Bush_Blueberry_Small' from GlobalAssets (attempt {attempts}).");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    if (attempts <= 3)
                        MelonLogger.Warning($"ApplyBuildingData: GlobalAssets access error: {ex.Message}");
                }

                if (attempts <= 3 || attempts % 20 == 0)
                    MelonLogger.Warning($"ApplyBuildingData: GlobalAssets not ready yet (attempt {attempts}/{maxAttempts}), retrying...");
                yield return new WaitForSeconds(2f);
            }

            if (templateBD == null)
            {
                MelonLogger.Error("ApplyBuildingData: Could not load Bush_Blueberry_Small from GlobalAssets after 2 minutes.");
                yield break;
            }

            MelonLogger.Msg("ApplyBuildingData: Found blueberry template. Applying to forageables...");

            // Gather ALL BuildingData fields from the template, including v1.1 additions
            var allBDFields = buildingDataType.GetFields(flags);
            var templateValues = new Dictionary<string, object>();
            foreach (var f in allBDFields)
            {
                if (f.IsStatic || f.IsLiteral) continue;
                try { templateValues[f.Name] = f.GetValue(templateBD); } catch { }
            }

            var f_identifier = buildingDataType.GetField("identifier", flags);
            var f_prefabEntries = buildingDataType.GetField("prefabEntries", flags);

            var templateEntries = f_prefabEntries?.GetValue(templateBD) as System.Collections.IList;
            if (templateEntries == null || templateEntries.Count == 0)
            {
                MelonLogger.Error("ApplyBuildingData: No prefabEntries found on template.");
                yield break;
            }

            var entryType = templateEntries[0].GetType();
            var f_entryPrefab = entryType.GetField("prefab", flags);

            // Get the blueberry template's ACTUAL prefab asset (not a scene clone)
            // This is what goes into prefabEntries for all non-blueberry forageables
            object blueberryPrefabAsset = f_entryPrefab?.GetValue(templateEntries[0]);
            string bbPrefabName = (blueberryPrefabAsset as GameObject)?.name ?? "NULL";
            MelonLogger.Msg($"ApplyBuildingData: Blueberry template prefab asset = '{bbPrefabName}'");

            // Log template diagnostics
            object tPlaceable = templateValues.ContainsKey("placeablePrefab") ? templateValues["placeablePrefab"] : null;
            object tBuildSite = templateValues.ContainsKey("buildSitePrefab") ? templateValues["buildSitePrefab"] : null;
            object tDeconSite = templateValues.ContainsKey("deconstructSitePrefab") ? templateValues["deconstructSitePrefab"] : null;
            object tDest = templateValues.ContainsKey("destinationPrefab") ? templateValues["destinationPrefab"] : null;
            MelonLogger.Msg($"ApplyBuildingData: Template placeablePrefab={tPlaceable != null}, buildSitePrefab={tBuildSite != null}, deconstructSitePrefab={tDeconSite != null}, destinationPrefab={tDest != null}");
            MelonLogger.Msg($"ApplyBuildingData: Template has {templateValues.Count} fields, {templateEntries.Count} prefabEntries");

            // Also get the ForageableResource.buildingDataIdentifier field for setting
            var f_bdIdentifier = typeof(ForageableResource).GetField("buildingDataIdentifier", flags);

            // Get the blueberry identifier string from the template
            string blueberryIdStr = f_identifier?.GetValue(templateBD) as string;
            MelonLogger.Msg($"ApplyBuildingData: Blueberry identifier='{blueberryIdStr}'");

            int count = 0;
            foreach (var obj in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                var comp = obj.GetComponent("ForageableResource");
                if (comp == null) continue;
                if (obj.name.ToLower().Contains("blueberry")) continue;
                if (obj.name.ToLower().Contains("deco")) continue;
                var bdField = comp.GetType().GetField("_buildingData", flags);
                if (bdField == null) continue;
                if (bdField.GetValue(comp) != null) continue;

                // Clone fields from the blueberry template BD, SKIPPING prefabEntries
                // and diagPrefabEntries to avoid copying DLC-tainted entry references
                var newBD = Activator.CreateInstance(buildingDataType);
                foreach (var kvp in templateValues)
                {
                    // Skip entry lists — we create fresh ones below
                    if (kvp.Key == "prefabEntries" || kvp.Key == "diagPrefabEntries") continue;

                    var field = buildingDataType.GetField(kvp.Key, flags);
                    if (field != null && !field.IsLiteral)
                    {
                        try { field.SetValue(newBD, kvp.Value); } catch { }
                    }
                }

                // Override identifier with this object's name
                f_identifier?.SetValue(newBD, obj.name.Replace("(Clone)", "").Trim());

                // Reuse the blueberry template's ACTUAL prefabEntries[0] entry directly.
                // Activator.CreateInstance-created entries have uninitialized dlcAsset structs
                // that cause Harmony's DMD wrapper for PREFAB() to NullRef.
                // Using the real template entry avoids this because it's properly initialized.
                var listType = typeof(List<>).MakeGenericType(entryType);
                var newList = (System.Collections.IList)Activator.CreateInstance(listType);
                newList.Add(templateEntries[0]); // The real blueberry entry, not a clone
                f_prefabEntries?.SetValue(newBD, newList);

                // Also set diagPrefabEntries to the template's diag entries (or empty)
                var f_diagEntries = buildingDataType.GetField("diagPrefabEntries", flags);
                if (f_diagEntries != null)
                {
                    var templateDiag = f_diagEntries.GetValue(templateBD) as System.Collections.IList;
                    if (templateDiag != null && templateDiag.Count > 0)
                        f_diagEntries.SetValue(newBD, templateDiag);
                    else
                    {
                        var emptyDiagList = (System.Collections.IList)Activator.CreateInstance(listType);
                        f_diagEntries.SetValue(newBD, emptyDiagList);
                    }
                }

                bdField.SetValue(comp, newBD);

                // Also set the buildingDataIdentifier string on ForageableResource itself
                // Use the blueberry's identifier so GlobalAssets lookup works in Input_RelocateObject
                if (f_bdIdentifier != null)
                    f_bdIdentifier.SetValue(comp, blueberryIdStr);

                count++;
                if (count <= 5)
                    MelonLogger.Msg($"ApplyBuildingData: Enabled transplantation for '{obj.name}' (bdId='{blueberryIdStr}')");
            }

            MelonLogger.Msg($"ApplyBuildingData: Done. Enabled {count} new forageables for transplantation.");
        }

        // (YearChangeWatcher removed — relocated/planted forageables inherit _buildingData from prefab)

        // =====================================================================
        // Priority helpers (shared between patches and save/load)
        // =====================================================================
        // Key by position so priorities survive building upgrades (same position, new instance)
        internal static int GetShackKey(ForagerShack shack)
        {
            var pos = shack.transform.position;
            return Mathf.RoundToInt(pos.x * 1000f + pos.z);
        }

        internal static int GetPriority(ForagerShack shack, ItemID itemID)
        {
            int id = GetShackKey(shack);
            if (shackPriorities.TryGetValue(id, out var dict))
            {
                if (dict.TryGetValue((int)itemID, out int val))
                    return val;
            }
            return DEFAULT_PRIORITY;
        }

        // Save priority value and apply score (called by arrow clicks)
        internal static void SetPriority(ForagerShack shack, ItemID itemID, int priority)
        {
            priority = Mathf.Clamp(priority, 1, 9);
            SetPriorityDirect(shack, itemID, priority);

            // Apply score — priority 1 = score 0 (toggle off equivalent)
            int score = PriorityToScore[priority];
            ApplyScoreToBucket(shack, itemID, (float)score);
        }

        // Save priority value only — no score application (used by SetPrioritizedPostfix to avoid loops)
        internal static void SetPriorityDirect(ForagerShack shack, ItemID itemID, int priority)
        {
            priority = Mathf.Clamp(priority, 1, 9);
            int id = GetShackKey(shack);
            if (!shackPriorities.TryGetValue(id, out var dict))
            {
                dict = new Dictionary<int, int>();
                shackPriorities[id] = dict;
            }
            dict[(int)itemID] = priority;
        }

        internal static void ApplyScoreToBucket(ForagerShack shack, ItemID itemID, float score)
        {
            try
            {
                var scoreField = typeof(ForagerShack).GetField("_scoreByForagingBucket", AllInstance);
                if (scoreField == null) return;

                var dict = scoreField.GetValue(shack) as System.Collections.IDictionary;
                if (dict == null) return;

                // Map ItemID to both normal and cultivated WorkBucketIdentifiers
                // Using int casts because we're going through IDictionary
                switch (itemID)
                {
                    case ItemID.Herbs:
                        SetBucketScore(dict, WorkBucketIdentifier.HerbsToForage, score);
                        SetBucketScore(dict, WorkBucketIdentifier.CultivatedHerbsToForage, score);
                        break;
                    case ItemID.Greens:
                        SetBucketScore(dict, WorkBucketIdentifier.GreensToForage, score);
                        SetBucketScore(dict, WorkBucketIdentifier.CultivatedGreensToForage, score);
                        break;
                    case ItemID.Nuts:
                        SetBucketScore(dict, WorkBucketIdentifier.NutsToForage, score);
                        SetBucketScore(dict, WorkBucketIdentifier.CultivatedNutsToForage, score);
                        break;
                    case ItemID.Mushroom:
                        SetBucketScore(dict, WorkBucketIdentifier.MushroomToForage, score);
                        SetBucketScore(dict, WorkBucketIdentifier.CultivatedMushroomToForage, score);
                        break;
                    case ItemID.Roots:
                        SetBucketScore(dict, WorkBucketIdentifier.RootsToForage, score);
                        SetBucketScore(dict, WorkBucketIdentifier.CultivatedRootsToForage, score);
                        break;
                    case ItemID.Willow:
                        SetBucketScore(dict, WorkBucketIdentifier.WillowToForage, score);
                        SetBucketScore(dict, WorkBucketIdentifier.CultivatedWillowToForage, score);
                        break;
                    case ItemID.Berries:
                        SetBucketScore(dict, WorkBucketIdentifier.BerriesToForage, score);
                        SetBucketScore(dict, WorkBucketIdentifier.CultivatedBerriesToForage, score);
                        break;
                    case ItemID.Eggs:
                        SetBucketScore(dict, WorkBucketIdentifier.EggsToForage, score);
                        break;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"ApplyScoreToBucket error: {ex.Message}");
            }
        }

        private static void SetBucketScore(System.Collections.IDictionary dict, WorkBucketIdentifier bucket, float score)
        {
            if (dict.Contains(bucket))
                dict[bucket] = score;
        }

        // Initialize default priorities for a shack (all items at 5)
        internal static void InitializeShackDefaults(ForagerShack shack)
        {
            int id = shack.GetInstanceID();
            if (shackPriorities.ContainsKey(id)) return;

            var dict = new Dictionary<int, int>();
            dict[(int)ItemID.Berries] = DEFAULT_PRIORITY;
            dict[(int)ItemID.Greens] = DEFAULT_PRIORITY;
            dict[(int)ItemID.Herbs] = DEFAULT_PRIORITY;
            dict[(int)ItemID.Mushroom] = DEFAULT_PRIORITY;
            dict[(int)ItemID.Roots] = DEFAULT_PRIORITY;
            dict[(int)ItemID.Nuts] = DEFAULT_PRIORITY;
            dict[(int)ItemID.Willow] = DEFAULT_PRIORITY;
            dict[(int)ItemID.Eggs] = DEFAULT_PRIORITY;
            shackPriorities[id] = dict;

            // Apply default scores
            foreach (var kvp in dict)
            {
                ApplyScoreToBucket(shack, (ItemID)kvp.Key, (float)PriorityToScore[kvp.Value]);
            }
        }
    }

    // =========================================================================
    // Year-round cultivation patches
    // =========================================================================
    public static class ForagerShackPatches
    {
        private static readonly BindingFlags AllInstance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static void GrowCultivatedItemPrefix(object __instance)
        {
            try
            {
                var field = __instance.GetType().GetField("canCultivatedThisYear", AllInstance);
                if (field != null)
                    field.SetValue(__instance, true);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"GrowCultivatedItemPrefix error: {ex.Message}");
            }
        }

        // Postfix on IsPrioritized: return true only when our priority > 1.
        // Priority 1 = score 0 = toggle OFF. Priority 2-9 = toggle ON.
        public static void IsPrioritizedPostfix(object __instance, ItemID itemID, ref bool __result)
        {
            try
            {
                var shack = __instance as ForagerShack;
                if (shack == null) return;
                int priority = TendedWildsMod.GetPriority(shack, itemID);
                __result = priority > 1;
            }
            catch { }
        }

        // Postfix on SetPrioritized: apply our 1-9 score when toggle ON,
        // leave at 0 when toggle OFF (kill switch).
        public static void SetPrioritizedPostfix(object __instance, ItemID itemID, bool val)
        {
            try
            {
                var shack = __instance as ForagerShack;
                if (shack == null) return;

                if (val)
                {
                    // Toggle ON — apply our 1-9 priority score
                    int priority = TendedWildsMod.GetPriority(shack, itemID);
                    if (priority <= 1)
                    {
                        // Was at 1 (off), bump to 5 (mid) when toggle is turned on
                        priority = 5;
                        TendedWildsMod.SetPriorityDirect(shack, itemID, priority);
                    }
                    int score = TendedWildsMod.PriorityToScore[priority];
                    TendedWildsMod.ApplyScoreToBucket(shack, itemID, (float)score);
                }
                // Toggle OFF — vanilla already set score to 0, leave it
            }
            catch { }
        }

        public static bool DestroyCultivatedItemsPrefix()
        {
            return false; // Skip winter destruction entirely
        }

        // UpdateScales self-cancels in winter. After the original runs (and potentially
        // CancelInvoke'd itself), we re-invoke to keep growth alive year-round.
        public static void UpdateScalesPostfix(object __instance)
        {
            try
            {
                var shack = __instance as ForagerShack;
                if (shack == null) return;

                // Check if there are cultivated items that still need growth (resource == null)
                var cultivatedItemsField = typeof(ForagerShack).GetField("cultivatedItems", AllInstance);
                if (cultivatedItemsField == null) return;
                var list = cultivatedItemsField.GetValue(shack) as System.Collections.IList;
                if (list == null || list.Count == 0) return;

                bool needsGrowth = false;
                foreach (var entry in list)
                {
                    if (entry == null) continue;
                    var resourceField = entry.GetType().GetField("resource", AllInstance);
                    if (resourceField == null) continue;
                    var resource = resourceField.GetValue(entry);
                    if (resource == null || resource.Equals(null))
                    {
                        // This entry has no resource yet — it needs UpdateScales to keep running
                        var itemField = entry.GetType().GetField("item", AllInstance);
                        if (itemField != null && itemField.GetValue(entry) != null)
                        {
                            needsGrowth = true;
                            break;
                        }
                    }
                }

                if (needsGrowth && !shack.IsInvoking("UpdateScales"))
                {
                    shack.InvokeRepeating("UpdateScales", 0.5f, UnityEngine.Random.Range(5f, 9.99f));
                }
            }
            catch { }
        }

        // OnSeasonChanged calls CancelInvoke("UpdateScales") in winter before DestroyCultivatedItems.
        // Since we skip DestroyCultivatedItems, we need to re-invoke UpdateScales.
        public static void OnSeasonChangedPostfix(object __instance)
        {
            try
            {
                var shack = __instance as ForagerShack;
                if (shack == null) return;

                // Re-invoke UpdateScales if there are items needing growth
                if (!shack.IsInvoking("UpdateScales"))
                {
                    var cultivatedItemsField = typeof(ForagerShack).GetField("cultivatedItems", AllInstance);
                    if (cultivatedItemsField == null) return;
                    var list = cultivatedItemsField.GetValue(shack) as System.Collections.IList;
                    if (list != null && list.Count > 0)
                    {
                        shack.InvokeRepeating("UpdateScales", 1f, UnityEngine.Random.Range(5f, 9.99f));
                    }
                }
            }
            catch { }
        }

        // Issue 2: After workers empty a cultivated forageable, reset growth state to allow regrowth
        // In vanilla, the destroy/reinit cycle at winter resets CultivatedForagedItem.item to null,
        // which allows GrowCultivatedItem's inner method to re-enter the grow path.
        // We null out .item and .resource on the matching CultivatedForagedItem entry,
        // then re-trigger GrowCultivatedItem() so the plant regrows without being destroyed.
        public static void OnCultivatedItemEmptyPostfix(object __instance, GameObject instigator)
        {
            try
            {
                var shack = __instance as ForagerShack;
                if (shack == null) return;

                var cultivatedItemsField = typeof(ForagerShack).GetField("cultivatedItems", AllInstance);
                if (cultivatedItemsField == null) return;

                var cultivatedList = cultivatedItemsField.GetValue(shack) as System.Collections.IList;
                if (cultivatedList == null) return;

                bool resetAny = false;
                foreach (var entry in cultivatedList)
                {
                    if (entry == null) continue;
                    var resourceField = entry.GetType().GetField("resource", AllInstance);
                    if (resourceField == null) continue;

                    var resource = resourceField.GetValue(entry) as ForageableResource;
                    // Match: the resource whose storage was just emptied
                    // We check if the resource's storage is empty or the resource matches the instigator context
                    if (resource != null)
                    {
                        // Check if this resource's storage is empty (it should be since OnCultivatedItemEmpty fired)
                        var itemStorageField = typeof(ReservableItemStorage).GetField("itemStorage", AllInstance);
                        if (itemStorageField != null)
                        {
                            var innerStorage = itemStorageField.GetValue(resource.storage) as ItemStorage;
                            if (innerStorage != null)
                            {
                                uint remaining = innerStorage.GetItemCountOfAllUnneededItems();
                                if (remaining > 0) continue; // This one still has items — not the emptied one
                            }
                        }

                        // Null out the item field to signal "needs regrowth"
                        var itemField = entry.GetType().GetField("item", AllInstance);
                        if (itemField != null)
                        {
                            itemField.SetValue(entry, null);
                            resetAny = true;
                        }

                        // Destroy the emptied ForageableResource object (mimics what vanilla destroy does per-entry)
                        try { UnityEngine.Object.Destroy(resource.gameObject); } catch { }
                        resourceField.SetValue(entry, null);

                        // Clear renderers list so GrowCultivatedItem can repopulate
                        var renderersField = entry.GetType().GetField("renderers", AllInstance);
                        if (renderersField != null)
                        {
                            var renderers = renderersField.GetValue(entry) as System.Collections.IList;
                            if (renderers != null)
                            {
                                foreach (var r in renderers)
                                {
                                    var renderer = r as Renderer;
                                    if (renderer != null && renderer.gameObject != null)
                                        try { UnityEngine.Object.Destroy(renderer.gameObject); } catch { }
                                }
                                renderers.Clear();
                            }
                        }

                        // Clear scale lists
                        var startScalesField = entry.GetType().GetField("startScales", AllInstance);
                        var endScalesField = entry.GetType().GetField("endScales", AllInstance);
                        (startScalesField?.GetValue(entry) as System.Collections.IList)?.Clear();
                        (endScalesField?.GetValue(entry) as System.Collections.IList)?.Clear();
                    }
                }

                if (resetAny)
                {
                    // Set canCultivatedThisYear = true and call GrowCultivatedItem to restart the cycle
                    var canField = typeof(ForagerShack).GetField("canCultivatedThisYear", AllInstance);
                    if (canField != null) canField.SetValue(shack, true);

                    var growMethod = typeof(ForagerShack).GetMethod("GrowCultivatedItem",
                        AllInstance | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                    if (growMethod != null)
                    {
                        growMethod.Invoke(shack, null);
                        MelonLogger.Msg("Greenhouse: Triggered regrowth after harvest.");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"OnCultivatedItemEmptyPostfix error: {ex}");
            }
        }
    }

    // =========================================================================
    // Priority UI patches (Features 2 & 3)
    // =========================================================================
    public static class PriorityUIPatches
    {
        private static readonly BindingFlags AllInstance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // Cached TMPro font asset found at runtime from existing game UI
        private static TMP_FontAsset cachedGameFont = null;

        // --- Priority row above icons + ensure widget visible ---
        public static void ProductivityInitPostfix(object __instance)
        {
            try
            {
                var shackField = typeof(UIForagingProductivitySubWidget).GetField("foragerShack", AllInstance);
                if (shackField == null) return;
                var shack = shackField.GetValue(__instance) as ForagerShack;
                if (shack == null) return;

                var comp = __instance as Component;
                if (comp == null) return;

                comp.gameObject.SetActive(true);
                var isActiveField = __instance.GetType().GetField("_isActive", AllInstance);
                if (isActiveField != null)
                    isActiveField.SetValue(__instance, true);

                // Tighten vertical gap: walk ancestors and log/reduce spacing
                try
                {
                    Transform t = comp.transform;
                    for (int d = 0; d < 6 && t != null; d++)
                    {
                        var vlg = t.GetComponent<VerticalLayoutGroup>();
                        var hlg = t.GetComponent<HorizontalLayoutGroup>();
                        var le = t.GetComponent<LayoutElement>();
                        if (vlg != null)
                        {
                            MelonLogger.Msg($"Layout[{d}] '{t.name}': VLG spacing={vlg.spacing}, padding=({vlg.padding.left},{vlg.padding.top},{vlg.padding.right},{vlg.padding.bottom})");
                            if (vlg.padding.top > 4)
                                vlg.padding = new RectOffset(vlg.padding.left, vlg.padding.right, 2, vlg.padding.bottom);
                            if (vlg.spacing > 4f)
                                vlg.spacing = 2f;
                        }
                        if (hlg != null)
                            MelonLogger.Msg($"Layout[{d}] '{t.name}': HLG spacing={hlg.spacing}, padding=({hlg.padding.left},{hlg.padding.top},{hlg.padding.right},{hlg.padding.bottom})");
                        if (le != null)
                            MelonLogger.Msg($"Layout[{d}] '{t.name}': LE prefH={le.preferredHeight}, minH={le.minHeight}");
                        t = t.parent;
                    }
                }
                catch { }

                TendedWildsMod.InitializeShackDefaults(shack);

                var iconDictField = typeof(UIForagingProductivitySubWidget).GetField("foragingIconDict", AllInstance);
                if (iconDictField == null) return;
                var iconDict = iconDictField.GetValue(__instance) as System.Collections.IDictionary;
                if (iconDict == null) return;

                // Get the togglesParent (Horizontal Layout containing icons)
                var togglesParentField = typeof(UIForagingProductivitySubWidget).GetField("togglesParent",
                    AllInstance | BindingFlags.Public);
                Transform togglesParent = togglesParentField?.GetValue(__instance) as Transform;

                MelonLogger.Msg($"PriorityInit: Processing {iconDict.Count} icons for shack '{shack.name}' key={TendedWildsMod.GetShackKey(shack)}");

                ForagerShack capturedShack = shack;
                var capturedIcons = new List<UIForagingIcon>();
                foreach (System.Collections.DictionaryEntry entry in iconDict)
                {
                    var icon = entry.Value as UIForagingIcon;
                    if (icon != null) capturedIcons.Add(icon);
                }

                MelonCoroutines.Start(DelayedCreatePriorityRow(comp, togglesParent, capturedIcons, capturedShack));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"ProductivityInitPostfix error: {ex}");
            }
        }

        private static IEnumerator DelayedCreatePriorityRow(Component widget, Transform togglesParent,
            List<UIForagingIcon> icons, ForagerShack shack)
        {
            yield return null;
            if (shack == null || widget == null) yield break;

            // Find and cache TMPro font
            if (cachedGameFont == null)
            {
                foreach (var tmp in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
                {
                    if (tmp != null && tmp.font != null)
                    {
                        cachedGameFont = tmp.font;
                        MelonLogger.Msg($"PriorityInit: Found TMPro font '{cachedGameFont.name}'");
                        break;
                    }
                }
            }

            try
            {
                // Find the parent that holds the Horizontal Layout (togglesParent's parent)
                // Hierarchy: ForagingProductivitySubWidget / ... / Horizontal Layout / icons
                Transform rowParent = (togglesParent != null) ? togglesParent.parent : widget.transform;

                // Destroy any existing priority row (always recreate for correct shack reference)
                string rowName = "TW_PriorityRow";
                for (int i = rowParent.childCount - 1; i >= 0; i--)
                {
                    var ch = rowParent.GetChild(i);
                    if (ch != null && ch.name == rowName)
                        UnityEngine.Object.Destroy(ch.gameObject);
                }

                // Create the priority row — horizontal layout above the icons
                var rowGO = new GameObject(rowName);
                rowGO.transform.SetParent(rowParent, false);

                // Insert before the togglesParent (icon row) so it appears above
                if (togglesParent != null)
                    rowGO.transform.SetSiblingIndex(togglesParent.GetSiblingIndex());

                var rowRT = rowGO.AddComponent<RectTransform>();
                rowRT.sizeDelta = new Vector2(0f, 22f);
                var rowLE = rowGO.AddComponent<LayoutElement>();
                rowLE.preferredHeight = 22f;
                rowLE.flexibleWidth = 1f;

                var rowHLG = rowGO.AddComponent<HorizontalLayoutGroup>();
                rowHLG.childAlignment = TextAnchor.MiddleCenter;
                rowHLG.spacing = 5f;
                rowHLG.childForceExpandWidth = true;
                rowHLG.childForceExpandHeight = true;
                rowHLG.padding = new RectOffset(2, 2, 0, 0);

                // Create one priority control per icon
                int created = 0;
                foreach (var icon in icons)
                {
                    if (icon == null) continue;

                    Item item = null;
                    try
                    {
                        var itemField = typeof(UIForagingIcon).GetField("itemDisplayed", AllInstance);
                        item = itemField?.GetValue(icon) as Item;
                    }
                    catch { }
                    if (item == null) continue;

                    ItemID capturedItemID = item.itemID;
                    ForagerShack capturedShack = shack;

                    // Each control: [▼] [num] [▲] horizontal
                    var cellGO = new GameObject("PriorityCell_" + item.itemID);
                    cellGO.transform.SetParent(rowGO.transform, false);
                    cellGO.AddComponent<RectTransform>();
                    var cellHLG = cellGO.AddComponent<HorizontalLayoutGroup>();
                    cellHLG.childAlignment = TextAnchor.MiddleCenter;
                    cellHLG.spacing = 0f;
                    cellHLG.childForceExpandWidth = true;
                    cellHLG.childForceExpandHeight = true;

                    // Down arrow
                    var downGO = CreateArrowCell(cellGO.transform, "▼",
                        new Color(0.18f, 0.18f, 0.2f, 0.85f), new Color(0.85f, 0.7f, 0.3f, 1f));

                    // Value
                    int priority = TendedWildsMod.GetPriority(shack, item.itemID);
                    var valGO = new GameObject("Value");
                    valGO.transform.SetParent(cellGO.transform, false);
                    valGO.AddComponent<RectTransform>();
                    var valBg = valGO.AddComponent<Image>();
                    valBg.color = new Color(0.08f, 0.08f, 0.1f, 0.9f);
                    valBg.raycastTarget = false;
                    var valTxtGO = new GameObject("Text");
                    valTxtGO.transform.SetParent(valGO.transform, false);
                    var valTxtRT = valTxtGO.AddComponent<RectTransform>();
                    valTxtRT.anchorMin = Vector2.zero;
                    valTxtRT.anchorMax = Vector2.one;
                    valTxtRT.sizeDelta = Vector2.zero;
                    valTxtRT.offsetMin = Vector2.zero;
                    valTxtRT.offsetMax = Vector2.zero;
                    var valTxt = valTxtGO.AddComponent<TextMeshProUGUI>();
                    if (cachedGameFont != null) valTxt.font = cachedGameFont;
                    valTxt.text = priority.ToString();
                    valTxt.fontSize = 12;
                    valTxt.fontStyle = FontStyles.Bold;
                    valTxt.alignment = TextAlignmentOptions.Center;
                    valTxt.color = Color.white;
                    valTxt.raycastTarget = false;
                    valTxt.enableWordWrapping = false;

                    // Up arrow
                    var upGO = CreateArrowCell(cellGO.transform, "▲",
                        new Color(0.18f, 0.18f, 0.2f, 0.85f), new Color(0.85f, 0.7f, 0.3f, 1f));

                    // Wire arrow clicks via EventTrigger (safe — no Button component)
                    var downTrigger = downGO.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                    var downEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
                    downEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerClick;
                    downEntry.callback.AddListener((data) =>
                    {
                        try
                        {
                            int cur = TendedWildsMod.GetPriority(capturedShack, capturedItemID);
                            if (cur > 1)
                            {
                                TendedWildsMod.SetPriority(capturedShack, capturedItemID, cur - 1);
                                if (valTxt != null) valTxt.text = (cur - 1).ToString();
                            }
                        }
                        catch { }
                    });
                    downTrigger.triggers.Add(downEntry);

                    var upTrigger = upGO.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                    var upEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
                    upEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerClick;
                    upEntry.callback.AddListener((data) =>
                    {
                        try
                        {
                            int cur = TendedWildsMod.GetPriority(capturedShack, capturedItemID);
                            if (cur < 9)
                            {
                                TendedWildsMod.SetPriority(capturedShack, capturedItemID, cur + 1);
                                if (valTxt != null) valTxt.text = (cur + 1).ToString();
                            }
                        }
                        catch { }
                    });
                    upTrigger.triggers.Add(upEntry);

                    // Vanilla toggle left untouched — our SetPrioritizedPostfix handles
                    // applying the 1-9 score whenever the game calls SetPrioritized

                    created++;
                }

                MelonLogger.Msg($"PriorityInit: created={created} priority controls in row");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"PriorityInit row creation error: {ex}");
            }
        }

        private static GameObject CreateArrowCell(Transform parent, string label, Color bgColor, Color textColor)
        {
            var go = new GameObject("Arrow_" + label);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 10f;
            var bg = go.AddComponent<Image>();
            bg.color = bgColor;
            bg.raycastTarget = true;
            var txtGO = new GameObject("Text");
            txtGO.transform.SetParent(go.transform, false);
            var txtRT = txtGO.AddComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero;
            txtRT.anchorMax = Vector2.one;
            txtRT.sizeDelta = Vector2.zero;
            txtRT.offsetMin = Vector2.zero;
            txtRT.offsetMax = Vector2.zero;
            var txt = txtGO.AddComponent<TextMeshProUGUI>();
            if (cachedGameFont != null) txt.font = cachedGameFont;
            txt.text = label;
            txt.fontSize = 10;
            txt.alignment = TextAlignmentOptions.Center;
            txt.color = textColor;
            txt.raycastTarget = false;
            txt.enableWordWrapping = false;
            return go;
        }

        // (AddPriorityControlsToIcon removed — replaced by row-based system above)
    }

    // =========================================================================
    // Display name patch (Feature 4)
    // =========================================================================
    public static class DisplayNamePatches
    {
        public static void SetBuildingDataRecordNamePostfix(object __instance)
        {
            try
            {
                var building = __instance as Building;
                if (building == null) return;

                var foragerShack = building as ForagerShack;
                if (foragerShack == null) return;

                if (building.tier >= 2)
                {
                    var resource = building as Resource;
                    if (resource != null)
                        resource.displayName = "Forager's Greenhouse";
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"SetBuildingDataRecordNamePostfix error: {ex.Message}");
            }
        }
    }

    // =========================================================================
    // Feature 5: Wild Planting system
    // =========================================================================
    public static class WildPlantingPatches
    {
        private static readonly BindingFlags AllInstance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // --- Static flags for inter-patch communication ---
        public static bool IsWildPlanting = false;
        public static ItemID WildPlantCostItemID = ItemID.Berries;
        public static int WildPlantCostAmount = 50;
        public static int WildPlantGoldCost = 25;
        public static string WildPlantTargetPrefab = "";  // prefab base name to spawn
        public static ForagerShack WildPlantSourceShack = null;

        // Blueberry BuildingData identifier, discovered at runtime
        private static string blueberryIdentifier = null;

        // Cached reference to the blueberry BuildingData's buildSitePrefab.
        // Used in ConstructPrefix to verify the incoming construction is
        // actually our wild plant placement, not some other building being placed.
        private static GameObject expectedBlueberryBuildSitePrefab = null;

        // Prefab cache (discovered at runtime like Forageable Transplantation)
        internal static Dictionary<string, GameObject> foragePrefabs = new Dictionary<string, GameObject>();

        // Tracks pending wild plant build sites: build site instanceID -> prefab base name
        private static Dictionary<int, WildPlantPending> pendingWildPlants =
            new Dictionary<int, WildPlantPending>();

        private class WildPlantPending
        {
            public string prefabBaseName;
            public ItemID itemID;
            public Vector3 position;
        }

        // --- Cost table ---
        private static Dictionary<ItemID, WildPlantCost> costTable = new Dictionary<ItemID, WildPlantCost>
        {
            { ItemID.Roots,    new WildPlantCost(150, 50, "roots_concentration_small_01") },   // Uncommon
            { ItemID.Herbs,    new WildPlantCost(250, 25, "herbs_patch_small_01") },        // Common
            { ItemID.Greens,   new WildPlantCost(250, 25, "greens_patch_small_01") },       // Common
            { ItemID.Mushroom, new WildPlantCost(150, 50, "mushroom_cluster_small_01") },   // Uncommon
            { ItemID.Nuts,     new WildPlantCost(100, 75, "bush_hazelnut_med01a") },        // Rare
            { ItemID.Willow,   new WildPlantCost(100, 75, "bush_willow_med01a") },          // Rare
            { ItemID.Berries,  new WildPlantCost(250, 25, "") }                             // Common
        };

        private static readonly string[] berryPrefabs = new string[]
        {
            "bush_hawthorn_med01a",
            "bush_sumac_med01a",
            "bush_blueberry_med01a"
        };

        private struct WildPlantCost
        {
            public int amount;
            public int goldCost;
            public string prefabName;
            public WildPlantCost(int a, int g, string p) { amount = a; goldCost = g; prefabName = p; }
        }

        // --- Hardcoded season windows per prefab (authoritative, not searched) ---
        public static readonly Dictionary<string, int[][]> prefabSeasonWindows = new Dictionary<string, int[][]>
        {
            { "roots_concentration_small_01",  new int[][] { new int[] { 265, 354 } } },
            { "herbs_patch_small_01",          new int[][] { new int[] { 78, 170 }, new int[] { 171, 264 } } },
            { "greens_patch_small_01",         new int[][] { new int[] { 78, 170 }, new int[] { 171, 264 } } },
            { "mushroom_cluster_small_01",     new int[][] { new int[] { 265, 354 } } },
            { "bush_hazelnut_med01a",          new int[][] { new int[] { 265, 354 } } },
            { "bush_willow_med01a",            new int[][] { new int[] { 78, 170 }, new int[] { 355, 77 } } },
            { "bush_hawthorn_med01a",          new int[][] { new int[] { 171, 264 }, new int[] { 265, 354 } } },
            { "bush_sumac_med01a",             new int[][] { new int[] { 171, 264 }, new int[] { 265, 354 } } },
            { "bush_blueberry_med01a",         new int[][] { new int[] { 171, 264 }, new int[] { 265, 354 } } }
        };

        // Postfix on ForagerShack.SetCultivatedItem — refresh the cost label
        public static void SetCultivatedItemPostfix(object __instance)
        {
            try
            {
                var shack = __instance as ForagerShack;
                if (shack == null || shack.tier < 2) return;

                // Find the TW_CostLabel in any active UI panel for this shack
                foreach (var widget in Resources.FindObjectsOfTypeAll<UISubwidgetForagerShack>())
                {
                    if (widget == null) continue;
                    var costLabel = widget.transform.Find("TW_CostLabel");
                    if (costLabel != null)
                    {
                        var sf = typeof(UISubwidgetForagerShack).GetField("foragerShack", AllInstance);
                        var widgetShack = sf?.GetValue(widget) as ForagerShack;
                        if (widgetShack == shack)
                            UpdateCostLabel(costLabel.gameObject, shack);
                    }
                }
            }
            catch { }
        }

        public static void Reset()
        {
            IsWildPlanting = false;
            WildPlantSourceShack = null;
            RestoreBlueberryMaterials();
            blueberryIdentifier = null;
            expectedBlueberryBuildSitePrefab = null;
            foragePrefabs.Clear();
            pendingWildPlants.Clear();
        }

        // Default wild forageable yields matching vanilla approximately
        private static uint GetDefaultYield(ItemID id)
        {
            switch (id)
            {
                case ItemID.Greens:   return 10;
                case ItemID.Herbs:    return 10;
                case ItemID.Berries:  return 10;
                case ItemID.Roots:    return 8;
                case ItemID.Mushroom: return 8;
                case ItemID.Nuts:     return 8;
                case ItemID.Willow:   return 6;
                default:              return 8;
            }
        }

        // Map ItemID to the correct Item subclass name string for the constructor
        public static Item CreateItemForID(ItemID id)
        {
            switch (id)
            {
                case ItemID.Roots:    return new Item("ItemRoots", ItemID.Roots);
                case ItemID.Herbs:    return new Item("ItemHerbs", ItemID.Herbs);
                case ItemID.Greens:   return new Item("ItemGreens", ItemID.Greens);
                case ItemID.Mushroom: return new Item("ItemMushroom", ItemID.Mushroom);
                case ItemID.Nuts:     return new Item("ItemNuts", ItemID.Nuts);
                case ItemID.Willow:   return new Item("ItemWillow", ItemID.Willow);
                case ItemID.Berries:  return new Item("ItemBerries", ItemID.Berries);
                default:              return new Item("ItemBerries", ItemID.Berries);
            }
        }

        // =====================================================================
        // Scout blueberry identifier and prefabs at scene load
        // =====================================================================
        public static IEnumerator ScoutBlueberryIdentifier()
        {
            yield return new WaitForSeconds(15f);

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

            // Verify Bush_Blueberry_Small exists in GlobalAssets before caching its identifier.
            // This is fast (GlobalAssets is a static asset store) and doesn't depend on
            // any blueberries actually being spawned on the map — works even on custom maps
            // with zero blueberry spawns.
            int scoutAttempts = 0;
            const int maxScoutAttempts = 60;  // 60 × 2s = 2 minutes
            while (string.IsNullOrEmpty(blueberryIdentifier) && scoutAttempts < maxScoutAttempts)
            {
                scoutAttempts++;
                try
                {
                    var bd = GlobalAssets.buildingSetupData?.GetBuildingData("Bush_Blueberry_Small");
                    if (bd != null)
                    {
                        blueberryIdentifier = "Bush_Blueberry_Small";
                        MelonLogger.Msg($"WildPlanting: Verified blueberry identifier '{blueberryIdentifier}' via GlobalAssets (attempt {scoutAttempts}).");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    if (scoutAttempts <= 3)
                        MelonLogger.Warning($"WildPlanting: GlobalAssets access error: {ex.Message}");
                }

                if (scoutAttempts <= 3 || scoutAttempts % 20 == 0)
                    MelonLogger.Warning($"WildPlanting: GlobalAssets not ready (attempt {scoutAttempts}/{maxScoutAttempts}), retrying...");
                yield return new WaitForSeconds(2f);
            }

            if (string.IsNullOrEmpty(blueberryIdentifier))
                MelonLogger.Warning("WildPlanting: Could not verify blueberry identifier via GlobalAssets after 2 minutes!");

            // Scout forageable prefabs (same as Forageable Transplantation)
            foreach (var obj in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (obj.scene.IsValid()) continue;
                var forageComp = obj.GetComponent("ForageableResource");
                if (forageComp == null) continue;
                string baseName = obj.name.Replace("(Clone)", "").Trim().ToLower();
                if (baseName.Contains("deco")) continue;
                if (!foragePrefabs.ContainsKey(baseName))
                    foragePrefabs[baseName] = obj;
            }

            // Scene fallback
            foreach (var obj in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (!obj.scene.IsValid()) continue;
                var forageComp = obj.GetComponent("ForageableResource");
                if (forageComp == null) continue;
                string baseName = obj.name.Replace("(Clone)", "").Trim().ToLower();
                if (baseName.Contains("deco")) continue;
                if (!foragePrefabs.ContainsKey(baseName))
                    foragePrefabs[baseName] = obj;
            }

            // Third source: read prefab fields from ForagerShack instances
            // These are serialized asset references that exist regardless of map content
            // Fixes maps that don't have certain forageable types spawned naturally
            try
            {
                string[] prefabFieldNames = new string[]
                {
                    "herbsPrefab", "nutsPrefab", "greensPrefab",
                    "medicinalRootsPrefab", "mushroomsPrefab",
                    "willowPrefab", "berriesPrefab"
                };

                foreach (var shack in Resources.FindObjectsOfTypeAll<ForagerShack>())
                {
                    if (shack == null) continue;
                    foreach (var fieldName in prefabFieldNames)
                    {
                        var field = typeof(ForagerShack).GetField(fieldName, flags);
                        if (field == null) continue;
                        var prefabObj = field.GetValue(shack) as ForageableResource;
                        if (prefabObj == null) continue;
                        string baseName = prefabObj.gameObject.name.Replace("(Clone)", "").Trim().ToLower();
                        if (!string.IsNullOrEmpty(baseName) && !baseName.Contains("deco") && !foragePrefabs.ContainsKey(baseName))
                        {
                            foragePrefabs[baseName] = prefabObj.gameObject;
                            MelonLogger.Msg($"WildPlanting: Found prefab '{baseName}' from ForagerShack.{fieldName}");
                        }
                    }
                    break; // Only need one shack — all have the same prefab references
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"WildPlanting: ForagerShack prefab scan failed: {ex.Message}");
            }

            MelonLogger.Msg($"WildPlanting: Scouted {foragePrefabs.Count} forageable prefabs.");
        }

        // =====================================================================
        // Plant button on UISubwidgetForagerShack (tier 2 only)
        // =====================================================================
        // Cached plant icon sprite — loaded from embedded base64 or Mods folder fallback
        private static Sprite plantIconSprite = null;
        private static bool plantIconAttempted = false;

        public static void CultivationInitPostfix(object __instance)
        {
            try
            {
                var comp = __instance as Component;
                if (comp == null) return;

                var shackField = typeof(UISubwidgetForagerShack).GetField("foragerShack", AllInstance);
                if (shackField == null) return;
                var shack = shackField.GetValue(__instance) as ForagerShack;
                if (shack == null || shack.tier < 2) return;

                // Check if plant icon already exists in cultivation row
                string plantName = "TW_PlantIcon";
                // Find the toggles parent (the row containing cultivation circles)
                // The CEToggle fields are direct children of the widget — find their parent
                var toggleBerriesField = typeof(UISubwidgetForagerShack).GetField("toggleBerries", AllInstance | BindingFlags.Public);
                Transform cultivationRow = null;
                if (toggleBerriesField != null)
                {
                    var berryToggle = toggleBerriesField.GetValue(__instance) as Component;
                    if (berryToggle != null)
                        cultivationRow = berryToggle.transform.parent;
                }

                if (cultivationRow == null) return;

                // Don't modify cultivation row layout — it uses a custom layout system

                // Check for existing — destroy and recreate for correct shack reference
                for (int i = comp.transform.childCount - 1; i >= 0; i--)
                {
                    var ch = comp.transform.GetChild(i);
                    if (ch != null && ch.name == plantName)
                        UnityEngine.Object.Destroy(ch.gameObject);
                }

                // Also clean up old-style container if it exists
                var oldContainer = comp.transform.Find("TW_PlantContainer");
                if (oldContainer != null)
                    UnityEngine.Object.Destroy(oldContainer.gameObject);

                // Load plant icon (cached) — try embedded base64 first, then Mods folder fallback
                if (plantIconSprite == null && !plantIconAttempted)
                {
                    plantIconAttempted = true;
                    try
                    {
                        // Try embedded base64 first
                        byte[] pngData = System.Convert.FromBase64String(PlantIconBase64.DATA);
                        var tex = new Texture2D(2, 2);
                        tex.LoadImage(pngData);
                        plantIconSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                            new Vector2(0.5f, 0.5f), 100f);
                        MelonLogger.Msg($"PlantIcon: Loaded {tex.width}x{tex.height} from embedded base64");
                    }
                    catch
                    {
                        // Fallback to Mods folder
                        try
                        {
                            string iconPath = System.IO.Path.Combine(
                                System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                                "TendedWilds_PlantIcon.png");
                            if (System.IO.File.Exists(iconPath))
                            {
                                byte[] pngData = System.IO.File.ReadAllBytes(iconPath);
                                var tex = new Texture2D(2, 2);
                                tex.LoadImage(pngData);
                                plantIconSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                    new Vector2(0.5f, 0.5f), 100f);
                                MelonLogger.Msg($"PlantIcon: Loaded from Mods folder fallback");
                            }
                        }
                        catch { }
                    }
                }

                // Create plant circle icon — insert as first child of cultivation row
                var plantGO = new GameObject(plantName);
                // Parent to the widget itself with absolute positioning
                plantGO.transform.SetParent(comp.transform, false);

                var plantRT = plantGO.AddComponent<RectTransform>();
                var plantLE = plantGO.AddComponent<LayoutElement>();
                plantLE.ignoreLayout = true;
                // Position at left side, vertically centered with cultivation circles
                plantRT.anchorMin = new Vector2(0f, 0f);
                plantRT.anchorMax = new Vector2(0f, 0f);
                plantRT.pivot = new Vector2(0f, 0.5f);
                plantRT.anchoredPosition = new Vector2(8f, 50f);
                plantRT.sizeDelta = new Vector2(48f, 48f);

                var plantImg = plantGO.AddComponent<Image>();
                if (plantIconSprite != null)
                {
                    plantImg.sprite = plantIconSprite;
                    plantImg.type = Image.Type.Simple;
                    plantImg.preserveAspect = false; // Let layout control size
                }
                else
                {
                    plantImg.color = new Color(0.2f, 0.5f, 0.2f, 0.9f);
                }
                plantImg.raycastTarget = true;

                // Force explicit size
                plantRT.sizeDelta = new Vector2(70f, 70f);

                // Click handler via EventTrigger (safe, no Button component)
                var capturedWidget = __instance;
                var trigger = plantGO.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                var entry = new UnityEngine.EventSystems.EventTrigger.Entry();
                entry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerClick;
                entry.callback.AddListener((data) =>
                {
                    try
                    {
                        var sf = typeof(UISubwidgetForagerShack).GetField("foragerShack", AllInstance);
                        var currentShack = sf?.GetValue(capturedWidget) as ForagerShack;
                        if (currentShack != null) OnPlantButtonClicked(currentShack);
                    }
                    catch (Exception ex) { MelonLogger.Error($"PlantWild click: {ex.Message}"); }
                });
                trigger.triggers.Add(entry);

                // Cost label below the cultivation row
                string costName = "TW_CostLabel";
                var existingCost = comp.transform.Find(costName);
                if (existingCost != null)
                    UnityEngine.Object.Destroy(existingCost.gameObject);

                TMP_FontAsset gameFont = null;
                float gameFontSize = 12f;
                foreach (var tmp in comp.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    if (tmp != null && tmp.font != null) { gameFont = tmp.font; gameFontSize = tmp.fontSize; break; }
                }

                var costGO = new GameObject(costName);
                costGO.transform.SetParent(comp.transform, false);
                var costRT = costGO.AddComponent<RectTransform>();
                var costLayoutEl = costGO.AddComponent<LayoutElement>();
                costLayoutEl.ignoreLayout = true;
                // Position at bottom center, just above the Storage separator
                costRT.anchorMin = new Vector2(0.5f, 0f);
                costRT.anchorMax = new Vector2(0.5f, 0f);
                costRT.pivot = new Vector2(0.5f, 0f);
                costRT.anchoredPosition = new Vector2(0f, 7f);
                costRT.sizeDelta = new Vector2(200f, 16f);
                var costTxt = costGO.AddComponent<TextMeshProUGUI>();
                if (gameFont != null) costTxt.font = gameFont;
                costTxt.fontSize = Mathf.Max(13, Mathf.RoundToInt(gameFontSize * 0.65f) + 5);
                costTxt.fontStyle = FontStyles.Normal;
                costTxt.alignment = TextAlignmentOptions.Center;
                costTxt.color = new Color(0.85f, 0.7f, 0.3f, 1f); // Gold
                costTxt.raycastTarget = false;
                costTxt.enableWordWrapping = false;

                UpdateCostLabel(costGO, shack);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"CultivationInitPostfix error: {ex.Message}");
            }
        }

        private static void UpdateCostLabel(GameObject costGO, ForagerShack shack)
        {
            try
            {
                var costTmp = costGO.GetComponent<TextMeshProUGUI>();
                if (costTmp == null) return;

                Item cultivated = shack.GetCultivatedItem();
                if (cultivated != null && costTable.TryGetValue(cultivated.itemID, out WildPlantCost cost))
                {
                    string itemName = cultivated.itemID.ToString();
                    string extra = cultivated.itemID == ItemID.Berries ? " (random)" : "";
                    costTmp.text = $"Cost: {cost.amount} {itemName}{extra} + {cost.goldCost}g";
                }
                else
                {
                    costTmp.text = "Select a crop to plant";
                }
            }
            catch { }
        }

        // Saved original blueberry materials for restoration after placement completes
        private static object savedBlueberryMaterials = null;

        private static IEnumerator DelayedRestoreBlueberryMaterials()
        {
            yield return null; // Wait one frame for placement UI to read materials
            RestoreBlueberryMaterials();
        }

        private static void RestoreBlueberryMaterials()
        {
            if (savedBlueberryMaterials == null || string.IsNullOrEmpty(blueberryIdentifier)) return;
            try
            {
                var bbData = GlobalAssets.buildingSetupData.GetBuildingData(blueberryIdentifier);
                var matField = bbData?.GetType().GetField("buildingMaterials", AllInstance);
                if (matField != null)
                    matField.SetValue(bbData, savedBlueberryMaterials);
            }
            catch { }
            savedBlueberryMaterials = null;
        }

        private static void OnPlantButtonClicked(ForagerShack shack)
        {
            try
            {
                if (string.IsNullOrEmpty(blueberryIdentifier))
                {
                    MelonLogger.Warning("WildPlanting: No blueberry identifier found. Cannot enter placement.");
                    return;
                }

                Item cultivated = shack.GetCultivatedItem();
                if (cultivated == null)
                {
                    MelonLogger.Warning("WildPlanting: No cultivated item selected.");
                    return;
                }

                if (!costTable.TryGetValue(cultivated.itemID, out WildPlantCost cost))
                {
                    MelonLogger.Warning($"WildPlanting: No cost entry for {cultivated.itemID}.");
                    return;
                }

                // Set static flags
                IsWildPlanting = true;
                WildPlantCostItemID = cultivated.itemID;
                WildPlantCostAmount = cost.amount;
                WildPlantGoldCost = cost.goldCost;
                WildPlantSourceShack = shack;

                // Resolve target prefab (berries get random roll)
                if (cultivated.itemID == ItemID.Berries)
                {
                    int roll = UnityEngine.Random.Range(0, 3);
                    WildPlantTargetPrefab = berryPrefabs[roll];
                    MelonLogger.Msg($"WildPlanting: Berry roll = {roll} -> {WildPlantTargetPrefab}");
                }
                else
                {
                    WildPlantTargetPrefab = cost.prefabName;
                }

                MelonLogger.Msg($"WildPlanting: Entering placement for {cultivated.itemID} ({WildPlantCostAmount}x cost, prefab={WildPlantTargetPrefab})");

                // Pre-patch blueberry BuildingData.buildingMaterials so placement UI shows correct cost
                var bbData = GlobalAssets.buildingSetupData.GetBuildingData(blueberryIdentifier);
                if (bbData != null)
                {
                    // Cache the blueberry buildSitePrefab for identity verification in ConstructPrefix.
                    // This lets us detect whether a Construct call is for our wild planting vs.
                    // some other building the player started placing afterward.
                    var bspField = bbData.GetType().GetField("buildSitePrefab", AllInstance);
                    if (bspField != null)
                        expectedBlueberryBuildSitePrefab = bspField.GetValue(bbData) as GameObject;

                    var matField = bbData.GetType().GetField("buildingMaterials", AllInstance);
                    if (matField != null)
                    {
                        savedBlueberryMaterials = matField.GetValue(bbData);
                        // Create a new list with our cost item
                        var entryType = typeof(BuildingMaterialEntry);
                        var newList = (System.Collections.IList)Activator.CreateInstance(
                            typeof(List<>).MakeGenericType(entryType));
                        var newEntry = Activator.CreateInstance(entryType);
                        var itemF = entryType.GetField("item", AllInstance);
                        var qtyF = entryType.GetField("quantity", AllInstance);
                        if (itemF != null) itemF.SetValue(newEntry, CreateItemForID(WildPlantCostItemID).name);
                        if (qtyF != null) qtyF.SetValue(newEntry, WildPlantCostAmount);
                        newList.Add(newEntry);
                        // Add gold cost entry
                        var goldEntry = Activator.CreateInstance(entryType);
                        if (itemF != null) itemF.SetValue(goldEntry, CreateItemForID(ItemID.GoldIngot).name);
                        if (qtyF != null) qtyF.SetValue(goldEntry, WildPlantGoldCost);
                        newList.Add(goldEntry);
                        matField.SetValue(bbData, newList);
                    }
                }

                // Push BeginBuildingPlacementSignal via InputManager's state machine
                var inputManager = UnitySingleton<GameManager>.Instance.inputManager;
                var smField = typeof(InputManager).GetField("inputStateMachine", AllInstance);
                if (smField != null)
                {
                    var stateMachine = smField.GetValue(inputManager);
                    var pushMethod = stateMachine.GetType().GetMethod("PushGlobalSignal",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (pushMethod != null)
                    {
                        pushMethod.Invoke(stateMachine, new object[] {
                            new BeginBuildingPlacementSignal(blueberryIdentifier, true, 1)
                        });
                    }
                }

                // Restore blueberry materials after a short delay.
                // The placement UI reads buildingMaterials synchronously when the signal
                // is processed, so by next frame it's safe to restore. ConstructPrefix
                // overrides materialsRequired independently, so the actual build cost
                // is never affected by the global BuildingData state.
                MelonCoroutines.Start(DelayedRestoreBlueberryMaterials());
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"OnPlantButtonClicked error: {ex}");
                IsWildPlanting = false;
                RestoreBlueberryMaterials();
            }
        }

        // =====================================================================
        // Construct prefix — intercept ConstructionData for wild planting
        // =====================================================================
        public static void ConstructPrefix(ref ConstructionData constructionData)
        {
            if (!IsWildPlanting) return;

            // Identity check: if this Construct call isn't actually our wild plant
            // placement, bail out. This handles the case where sticky mode leaves
            // IsWildPlanting = true but the player exits placement and starts
            // building something else (e.g., Windmill). Without this guard we'd
            // hijack the windmill's cost and prefab.
            if (expectedBlueberryBuildSitePrefab != null
                && constructionData.buildSitePrefab != expectedBlueberryBuildSitePrefab)
            {
                MelonLogger.Msg($"WildPlanting: Construct intercept skipped — buildSitePrefab mismatch " +
                    $"(got '{(constructionData.buildSitePrefab != null ? constructionData.buildSitePrefab.name : "null")}', " +
                    $"expected '{expectedBlueberryBuildSitePrefab.name}'). Clearing sticky mode.");
                IsWildPlanting = false;
                RestoreBlueberryMaterials();
                return;
            }

            try
            {
                MelonLogger.Msg($"WildPlanting: Intercepting Construct. Cost={WildPlantCostAmount}x {WildPlantCostItemID}, prefab={WildPlantTargetPrefab}");

                // For berries, re-roll if this is a multi-placement (shouldn't happen with count=1, but safety)
                if (WildPlantCostItemID == ItemID.Berries)
                {
                    int roll = UnityEngine.Random.Range(0, 3);
                    WildPlantTargetPrefab = berryPrefabs[roll];
                    MelonLogger.Msg($"WildPlanting: Berry Construct roll = {roll} -> {WildPlantTargetPrefab}");
                }

                // Replace materialsRequired with our custom cost
                var newMaterials = new Dictionary<Item, int>();
                newMaterials.Add(CreateItemForID(WildPlantCostItemID), WildPlantCostAmount);
                newMaterials.Add(CreateItemForID(ItemID.GoldIngot), WildPlantGoldCost);
                // Add a small amount of work (builder labor)
                newMaterials.Add(new ItemWorkUnit(), 10);
                constructionData.materialsRequired = newMaterials;

                // Swap prefabToConstruct to the correct forageable prefab
                if (foragePrefabs.TryGetValue(WildPlantTargetPrefab, out GameObject prefab))
                {
                    constructionData.prefabToConstruct = prefab;
                    MelonLogger.Msg($"WildPlanting: Set prefabToConstruct to '{prefab.name}'");
                }
                else
                {
                    MelonLogger.Warning($"WildPlanting: Prefab '{WildPlantTargetPrefab}' not found in cache!");
                }

                // Record pending wild plant keyed by position (we'll match in completion handler)
                // Store data we need at completion time
                pendingWildPlants[constructionData.position.GetHashCode()] = new WildPlantPending
                {
                    prefabBaseName = WildPlantTargetPrefab,
                    itemID = WildPlantCostItemID,
                    position = constructionData.position
                };

                // Keep IsWildPlanting = true for sticky placement mode
                // Blueberry materials are restored by DelayedRestoreBlueberryMaterials
                // after OnPlantButtonClicked; ConstructPrefix handles actual costs independently
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"ConstructPrefix error: {ex}");
                IsWildPlanting = false;
                RestoreBlueberryMaterials();
            }
        }

        // =====================================================================
        // Build completion handler — spawn forageable with season windows
        // =====================================================================
        public static void OnBuiltPrefabInstantiatedPostfix(object __instance, GameObject builtInstanceOrNull)
        {
            if (pendingWildPlants.Count == 0) return;
            if (builtInstanceOrNull == null) return;

            try
            {
                // Match by position: the build site's position should match a pending entry
                Component buildSiteComp = __instance as Component;
                if (buildSiteComp == null) return;

                Vector3 pos = buildSiteComp.transform.position;
                int posHash = pos.GetHashCode();

                WildPlantPending pending = null;
                int matchKey = -1;

                // Try exact hash match first
                if (pendingWildPlants.TryGetValue(posHash, out pending))
                {
                    matchKey = posHash;
                }
                else
                {
                    // Fallback: proximity match
                    foreach (var kvp in new Dictionary<int, WildPlantPending>(pendingWildPlants))
                    {
                        if (Vector3.Distance(pos, kvp.Value.position) < 3f)
                        {
                            pending = kvp.Value;
                            matchKey = kvp.Key;
                            break;
                        }
                    }
                }

                if (pending == null) return;

                pendingWildPlants.Remove(matchKey);
                MelonLogger.Msg($"WildPlanting: Build complete for '{pending.prefabBaseName}' at {pos}");

                var reflFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

                // Bug 3 fix: Clear existing seasons before adding ours (prevents duplicates)
                var seasonalComp = builtInstanceOrNull.GetComponent("SeasonalComponentBase");
                if (seasonalComp != null)
                {
                    var sType = (seasonalComp as Component).GetType();
                    var seasonsProp = sType.GetProperty("seasons", reflFlags);
                    if (seasonsProp != null)
                    {
                        var existingSeasons = seasonsProp.GetValue(seasonalComp) as System.Collections.IList;
                        if (existingSeasons != null)
                        {
                            int preClear = existingSeasons.Count;
                            existingSeasons.Clear();
                            MelonLogger.Msg($"WildPlanting: Cleared {preClear} pre-existing season window(s).");
                        }
                    }

                    if (prefabSeasonWindows.TryGetValue(pending.prefabBaseName, out int[][] windows))
                    {
                        var windowList = new List<int[]>(windows);
                        ApplySeasonWindows(seasonalComp as Component, windowList);
                        MelonLogger.Msg($"WildPlanting: Applied {windowList.Count} hardcoded season window(s) for '{pending.prefabBaseName}'.");
                    }

                    // Bug 2 fix: Manually call HandleDayChanged to kick-start seasonal evaluation
                    var handleDay = sType.GetMethod("HandleDayChanged", reflFlags);
                    if (handleDay != null)
                    {
                        var gm = UnitySingleton<GameManager>.Instance;
                        if (gm != null && gm.timeManager != null)
                        {
                            var dateObj = gm.timeManager.GetType().GetProperty("currentDate", reflFlags)?.GetValue(gm.timeManager);
                            if (dateObj != null)
                            {
                                var dayProp = dateObj.GetType().GetProperty("dayOfYear", reflFlags);
                                var dayField = dateObj.GetType().GetField("dayOfYear", reflFlags);
                                int currentDay = -1;
                                if (dayProp != null) currentDay = (int)dayProp.GetValue(dateObj);
                                else if (dayField != null) currentDay = (int)dayField.GetValue(dateObj);

                                if (currentDay > 0)
                                {
                                    handleDay.Invoke(seasonalComp, new object[] { currentDay });
                                    MelonLogger.Msg($"WildPlanting: Called HandleDayChanged({currentDay}) to init seasonal state.");
                                }
                            }
                        }
                    }
                }

                // Reset initialization flags so the forageable properly initializes
                var forageComp = builtInstanceOrNull.GetComponent("ForageableResource");
                if (forageComp != null)
                {
                    var fType = (forageComp as Component).GetType();

                    var itemsAddedField = fType.GetField("itemsAddedForSeason", reflFlags);
                    var initializedField = fType.GetField("initialized", reflFlags);
                    if (itemsAddedField != null) itemsAddedField.SetValue(forageComp, false);
                    if (initializedField != null) initializedField.SetValue(forageComp, false);

                    // Call SetRandomReplenishRateOnSpawn if available
                    var setRandom = fType.GetMethod("SetRandomReplenishRateOnSpawn", reflFlags);
                    if (setRandom != null)
                    {
                        try { setRandom.Invoke(forageComp, null); }
                        catch { }
                    }

                    // Bug 1 fix: Set replenish rates for wild planted forageables
                    var setAmount = fType.GetMethod("SetAmountToReplenish", reflFlags, null,
                        new Type[] { typeof(Item), typeof(uint) }, null);
                    var setMaxAmount = fType.GetMethod("SetMaxAmountToReplenish", reflFlags, null,
                        new Type[] { typeof(Item), typeof(uint) }, null);

                    if (setAmount != null)
                    {
                        uint yield = GetDefaultYield(pending.itemID);
                        Item costItem = CreateItemForID(pending.itemID);
                        try
                        {
                            setAmount.Invoke(forageComp, new object[] { costItem, yield });
                            if (setMaxAmount != null)
                                setMaxAmount.Invoke(forageComp, new object[] { costItem, yield });
                            MelonLogger.Msg($"WildPlanting: Set replenish rate={yield} for {pending.itemID}");
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Warning($"WildPlanting: SetAmountToReplenish failed: {ex.Message}");
                        }
                    }

                    MelonLogger.Msg($"WildPlanting: Spawn finalized for '{pending.prefabBaseName}'");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"OnBuiltPrefabInstantiatedPostfix error: {ex}");
            }
        }

        // (Diagnostic coroutine removed — bugs fixed directly in spawn handler)

        // =====================================================================
        // Season window helpers (CopySeasonWindows/ApplySeasonWindows used by relocation)
        // =====================================================================
        public static List<int[]> CopySeasonWindows(Component seasonalComp)
        {
            var result = new List<int[]>();
            try
            {
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                var seasonsProp = seasonalComp.GetType().GetProperty("seasons", flags);
                if (seasonsProp == null) return result;

                var seasonsList = seasonsProp.GetValue(seasonalComp) as System.Collections.IList;
                if (seasonsList == null || seasonsList.Count == 0) return result;

                var pairType = seasonsList[0].GetType();
                var firstField = pairType.GetField("first", flags);
                var secondField = pairType.GetField("second", flags);
                if (firstField == null || secondField == null) return result;

                foreach (var pair in seasonsList)
                {
                    int start = (int)firstField.GetValue(pair);
                    int end = (int)secondField.GetValue(pair);
                    result.Add(new int[] { start, end });
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CopySeasonWindows failed: {ex.Message}");
            }
            return result;
        }

        public static void ApplySeasonWindows(Component seasonalComp, List<int[]> windows)
        {
            if (windows == null || windows.Count == 0) return;
            try
            {
                var addSeason = seasonalComp.GetType().GetMethod("AddSeason",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (addSeason == null) return;

                foreach (var window in windows)
                    addSeason.Invoke(seasonalComp, new object[] { window[0], window[1] });
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"ApplySeasonWindows failed: {ex.Message}");
            }
        }
    }

    // =========================================================================
    // Relocation system (absorbed from Forageable Transplantation)
    // =========================================================================
    public static class RelocationPatches
    {
        public static readonly BindingFlags flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        public static Dictionary<int, PendingRelocation> PendingRelocations =
            new Dictionary<int, PendingRelocation>();

        public class PendingRelocation
        {
            public int instanceId;
            public string baseName;
            public Vector3 destination;
            public GameObject nativeConstructSite;
            public System.Collections.IDictionary replenishRates;
            public System.Collections.IDictionary maxReplenishRates;
            public List<int[]> seasonWindows;
        }

        // --- Issue 3: Null-safe prefix for UIHarvestableResourceWindow.Relocate ---
        // The vanilla code does: forageableResource.buildingData.identifier which NullRefs
        // if buildingData is null. We replace the entire method with a null-safe version.
        public static bool UIRelocatePrefix(object __instance)
        {
            try
            {
                // Read the 'resource' field on the window
                var resourceField = __instance.GetType().GetField("resource", flags);
                if (resourceField == null) { MelonLogger.Warning("UIRelocatePrefix: 'resource' field not found"); return true; }
                var resource = resourceField.GetValue(__instance) as Resource;
                if (resource == null) { MelonLogger.Warning("UIRelocatePrefix: resource is null"); return false; }

                string buildingDataIdentifier = null;
                var forageableResource = resource as ForageableResource;
                if (forageableResource != null)
                {
                    var bd = forageableResource.buildingData;
                    if (bd != null)
                    {
                        var idField = bd.GetType().GetField("identifier", flags);
                        buildingDataIdentifier = idField?.GetValue(bd) as string;
                    }
                    else
                    {
                        MelonLogger.Warning($"UIRelocatePrefix: buildingData is null for '{forageableResource.name}'. ApplyBuildingData may not have run yet.");
                        return false; // Skip vanilla method — prevents NullRef and input trap
                    }
                }

                // Call InputManager.BeginObjectRelocation safely
                var inputManager = UnitySingleton<GameManager>.Instance?.inputManager;
                if (inputManager != null)
                {
                    inputManager.BeginObjectRelocation(resource.gameObject, buildingDataIdentifier);
                    MelonLogger.Msg($"UIRelocatePrefix: Initiated relocation for '{resource.name}' with id='{buildingDataIdentifier}'");
                }

                return false; // Skip original method entirely — we handled it
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"UIRelocatePrefix error: {ex}");
                return false; // Don't run original either — prevents input trap
            }
        }

        // --- BuildManager.Relocate prefix ---
        public static void RelocatePrefix(object __instance, object deconstructionData, object constructionData)
        {
            MelonLogger.Msg($"RelocatePrefix ENTERED: deconstructionData={deconstructionData?.GetType().Name ?? "null"}, constructionData={constructionData?.GetType().Name ?? "null"}");
            try
            {
                if (constructionData == null || deconstructionData == null)
                {
                    MelonLogger.Msg("RelocatePrefix: null data, returning.");
                    return;
                }
                var f_sceneObject = deconstructionData.GetType().GetField("sceneObject", flags);
                if (f_sceneObject == null) return;
                var sceneObj = f_sceneObject.GetValue(deconstructionData) as GameObject;
                if (sceneObj == null) return;
                var forageComp = sceneObj.GetComponent("ForageableResource");
                if (forageComp == null) return;
                if (sceneObj.name.ToLower().Contains("blueberry")) return;
                if (sceneObj.name.ToLower().Contains("deco")) return;

                int instanceId = sceneObj.GetInstanceID();
                if (PendingRelocations.ContainsKey(instanceId)) return;

                var baseName = sceneObj.name.Replace("(Clone)", "").Trim().ToLower();
                var f_position = constructionData.GetType().GetField("position", flags);
                var destPos = f_position != null ? (Vector3)f_position.GetValue(constructionData) : Vector3.zero;

                if (Vector3.Distance(sceneObj.transform.position, destPos) < 5f) return;

                System.Collections.IDictionary copiedRates = null;
                System.Collections.IDictionary copiedMaxRates = null;

                var fType = (forageComp as Component).GetType();
                var replenishF = fType.GetField("itemToReplenishRateDict", flags);
                var maxReplenishF = fType.GetField("itemToMaxReplenishRateDict", flags);

                if (replenishF != null)
                {
                    var src = replenishF.GetValue(forageComp) as System.Collections.IDictionary;
                    if (src != null && src.Count > 0)
                    {
                        var cloned = (System.Collections.IDictionary)Activator.CreateInstance(src.GetType());
                        foreach (System.Collections.DictionaryEntry e in src) cloned[e.Key] = e.Value;
                        copiedRates = cloned;
                    }
                }

                if (maxReplenishF != null)
                {
                    var src = maxReplenishF.GetValue(forageComp) as System.Collections.IDictionary;
                    if (src != null && src.Count > 0)
                    {
                        var cloned = (System.Collections.IDictionary)Activator.CreateInstance(src.GetType());
                        foreach (System.Collections.DictionaryEntry e in src) cloned[e.Key] = e.Value;
                        copiedMaxRates = cloned;
                    }
                }

                // Use hardcoded season windows based on prefab name
                List<int[]> copiedSeasonWindows = null;
                if (WildPlantingPatches.prefabSeasonWindows.TryGetValue(baseName, out int[][] hardcoded))
                {
                    copiedSeasonWindows = new List<int[]>(hardcoded);
                }
                else
                {
                    // Fallback: try to copy from the SeasonalComponentBase on the original object
                    var seasonalComp = sceneObj.GetComponent("SeasonalComponentBase");
                    if (seasonalComp != null)
                        copiedSeasonWindows = WildPlantingPatches.CopySeasonWindows(seasonalComp as Component);
                }

                PendingRelocations[instanceId] = new PendingRelocation
                {
                    instanceId = instanceId,
                    baseName = baseName,
                    destination = destPos,
                    replenishRates = copiedRates,
                    maxReplenishRates = copiedMaxRates,
                    seasonWindows = copiedSeasonWindows
                };

                MelonLogger.Msg($"RelocatePrefix: Recorded '{baseName}' (id={instanceId}) -> {destPos}");
            }
            catch (Exception ex) { MelonLogger.Error($"RelocatePrefix error: {ex}"); }
        }

        // --- BuildSite.Initialize postfix ---
        public static void BuildSiteInitializePostfix(object __instance, object __0)
        {
            try
            {
                Component buildSiteComp = __instance as Component;
                if (buildSiteComp == null || __0 == null) return;
                if (PendingRelocations.Count == 0) return;

                var f_position = __0.GetType().GetField("position", flags);
                if (f_position == null) return;
                var position = (Vector3)f_position.GetValue(__0);

                foreach (var kvp in new Dictionary<int, PendingRelocation>(PendingRelocations))
                {
                    var pending = kvp.Value;
                    if (pending.nativeConstructSite == null
                        && Vector3.Distance(position, pending.destination) < 2f)
                    {
                        pending.nativeConstructSite = buildSiteComp.gameObject;
                        MelonLogger.Msg($"BuildSiteInitializePostfix: Linked site for '{pending.baseName}' at {position}");
                        return;
                    }
                }
            }
            catch (Exception ex) { MelonLogger.Error($"BuildSiteInitializePostfix error: {ex}"); }
        }

        // --- OnBuiltPrefabInstantiated postfix (relocation completion) ---
        public static void OnBuiltPrefabInstantiatedPostfix(object __instance, GameObject builtInstanceOrNull)
        {
            if (PendingRelocations.Count == 0) return;

            try
            {
                Component buildSiteComp = __instance as Component;
                if (buildSiteComp == null) return;

                foreach (var kvp in new Dictionary<int, PendingRelocation>(PendingRelocations))
                {
                    var pending = kvp.Value;
                    if (pending.nativeConstructSite != null
                        && pending.nativeConstructSite == buildSiteComp.gameObject)
                    {
                        MelonLogger.Msg($"Relocation complete: '{pending.baseName}' (id={kvp.Key}). Spawning.");
                        PendingRelocations.Remove(kvp.Key);
                        SpawnForageableAtDestination(pending.baseName, pending);
                        return;
                    }
                }
            }
            catch (Exception ex) { MelonLogger.Error($"Relocation OnBuiltPrefabInstantiated error: {ex}"); }
        }

        // --- Spawn the relocated forageable ---
        public static void SpawnForageableAtDestination(string baseName, PendingRelocation pending)
        {
            MelonLogger.Msg($"SpawnForageableAtDestination: '{baseName}' at {pending.destination}");

            GameObject prefab;
            if (!WildPlantingPatches.foragePrefabs.TryGetValue(baseName, out prefab))
            {
                MelonLogger.Error($"No prefab found for '{baseName}'!");
                return;
            }
            if (prefab == null)
            {
                MelonLogger.Error($"Prefab for '{baseName}' is null!");
                WildPlantingPatches.foragePrefabs.Remove(baseName);
                return;
            }

            GameObject spawned = GameObject.Instantiate(prefab, pending.destination, Quaternion.identity);
            spawned.name = prefab.name.Replace("(Clone)", "").Trim();

            var forageComp = spawned.GetComponent("ForageableResource");
            if (forageComp != null)
            {
                var fType = (forageComp as Component).GetType();

                var setRandom = fType.GetMethod("SetRandomReplenishRateOnSpawn", flags);
                if (setRandom != null)
                    try { setRandom.Invoke(forageComp, null); } catch { }

                if (pending.replenishRates != null && pending.replenishRates.Count > 0)
                {
                    var setAmount = fType.GetMethod("SetAmountToReplenish", flags, null,
                        new Type[] { typeof(Item), typeof(uint) }, null);
                    if (setAmount != null)
                    {
                        foreach (System.Collections.DictionaryEntry entry in pending.replenishRates)
                            try { setAmount.Invoke(forageComp, new object[] { entry.Key, entry.Value }); } catch { }
                    }
                    else
                    {
                        var rf = fType.GetField("itemToReplenishRateDict", flags);
                        if (rf != null) rf.SetValue(forageComp, pending.replenishRates);
                        var mrf = fType.GetField("itemToMaxReplenishRateDict", flags);
                        if (mrf != null && pending.maxReplenishRates != null)
                            mrf.SetValue(forageComp, pending.maxReplenishRates);
                    }
                }

                var itemsAddedField = fType.GetField("itemsAddedForSeason", flags);
                var initializedField = fType.GetField("initialized", flags);
                if (itemsAddedField != null) itemsAddedField.SetValue(forageComp, false);
                if (initializedField != null) initializedField.SetValue(forageComp, false);
            }

            // Apply season windows
            var seasonalComp = spawned.GetComponent("SeasonalComponentBase");
            if (seasonalComp != null)
            {
                if (pending.seasonWindows != null && pending.seasonWindows.Count > 0)
                    WildPlantingPatches.ApplySeasonWindows(seasonalComp as Component, pending.seasonWindows);
                else
                {
                    // Use hardcoded windows as fallback
                    if (WildPlantingPatches.prefabSeasonWindows.TryGetValue(baseName, out int[][] fallback))
                        WildPlantingPatches.ApplySeasonWindows(seasonalComp as Component, new List<int[]>(fallback));
                }
            }

            spawned.SetActive(true);
            MelonLogger.Msg($"SpawnForageableAtDestination: SUCCESS - '{baseName}' at {pending.destination}");

            MelonCoroutines.Start(CleanupBlueberry(pending.destination, spawned));
        }

        private static IEnumerator CleanupBlueberry(Vector3 destination, GameObject keepObj)
        {
            yield return new WaitForSeconds(0.5f);
            foreach (var obj in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (obj == keepObj) continue;
                if (Vector3.Distance(obj.transform.position, destination) < 3f)
                    if (obj.GetComponent("ForageableResource") != null && obj.name.ToLower().Contains("blueberry"))
                    {
                        MelonLogger.Msg($"CleanupBlueberry: Destroying {obj.name}");
                        GameObject.Destroy(obj);
                    }
            }
        }
    }

}

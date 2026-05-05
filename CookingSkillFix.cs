using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CookingSkillFix
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("org.bepinex.plugins.cooking", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("gravebear.odinsfoodbarrels", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID    = "fix.cookingskillfix";
        public const string PluginName    = "CookingSkillFix";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log = null!;

        // Vanilla items that Fix 2 should never touch.
        internal static readonly HashSet<string> VanillaItemNames = new HashSet<string>
        {
            "Blueberries","Raspberry","Cloudberry","Carrot","Turnip","Onion","Barley",
            "BarleyFlour","Flax","Mushroom","MushroomBlue","MushroomYellow","Thistle",
            "Dandelion","Honey","RoyalJelly","FishRaw","SerpentMeat","NeckTail",
            "DeerMeat","BoarMeat","WolfMeat","LoxMeat","ChickenMeat","HareMeat",
            "BugMeat","CookedMeat","CookedDeerMeat","CookedBoarMeat","CookedLoxMeat",
            "CookedWolfMeat","CookedChickenMeat","CookedHareMeat","CookedBugMeat",
            "CookedFish","CookedSerpentMeat","NeckTailGrilled","CookedEgg",
            "FishCooked","SerpentMeatCooked","BlackSoup","BloodPudding","Bread",
            "CarrotSoup","DeerStew","FishWraps","LoxPie","MeatPlatter","MinotaurBroth",
            "MisthareSupreme","MushroomOmelette","OnionSoup","QueensJam","Salad",
            "SeekerAspic","ShocklateSmoothie","TurnipStew","WolfSkewer","YggdrasilPorridge",
            "Eyescream","FishAndBread","FishNBread","MagecapDishSoup","JotunPuffs",
            "Sap","Egg","ChickenEgg","AsksvinEgg","VultureEgg",
        };

        private void Awake()
        {
            Log = Logger;
            new Harmony(PluginGUID).PatchAll();
            Log.LogInfo("CookingSkillFix loaded.");

            if (IsModLoaded("gravebear.odinsfoodbarrels"))
                RegisterValharvestBoxes();
            else
                Log.LogInfo("CookingSkillFix: OdinsFoodBarrels not found, skipping food box fix.");
        }

        private static bool IsModLoaded(string guid)
        {
            foreach (var kv in BepInEx.Bootstrap.Chainloader.PluginInfos)
                if (kv.Key == guid) return true;
            return false;
        }

        private static void RegisterValharvestBoxes()
        {
            try
            {
                Assembly? odinAssembly = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "OdinsFoodBarrels")
                    {
                        odinAssembly = asm;
                        break;
                    }
                }

                if (odinAssembly == null)
                {
                    Log.LogWarning("CookingSkillFix: Could not find OdinsFoodBarrels assembly.");
                    return;
                }

                Type? restrictionsType = odinAssembly.GetType("OdinsFoodBarrels.ContainerRestrictions");
                if (restrictionsType == null)
                {
                    foreach (Type t in odinAssembly.GetTypes())
                    {
                        if (t.GetMethod("SetContainerRestrictions") != null)
                        {
                            restrictionsType = t;
                            break;
                        }
                    }
                }

                if (restrictionsType == null)
                {
                    Log.LogWarning("CookingSkillFix: Could not find ContainerRestrictions type.");
                    return;
                }

                MethodInfo? setMethod = restrictionsType.GetMethod(
                    "SetContainerRestrictions",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic
                );

                if (setMethod == null)
                {
                    Log.LogWarning("CookingSkillFix: Could not find SetContainerRestrictions method.");
                    return;
                }

                var parameters = setMethod.GetParameters();
                Type dictType = parameters[1].ParameterType;
                Type[] typeArgs = dictType.GetGenericArguments();

                // Box → allowed item name, max stack size 10 (matches OdinsFoodBarrels vanilla barrels)
                var boxes = new Dictionary<string, string>
                {
                    { "piece_garlicBox", "garlic"  },
                    { "piece_pepperBox", "pepper"  },
                    { "piece_potatoBox", "potato"  },
                    { "piece_tomatoBox", "tomato"  },
                    { "piece_saltBox",   "salt"    },
                    { "piece_appleBox",  "apple"   },
                };

                foreach (var box in boxes)
                {
                    try
                    {
                        // OdinsFoodBarrels uses Dictionary<string, HashSet<string>>
                        object dict = Activator.CreateInstance(dictType)!;
                        MethodInfo addMethod = dictType.GetMethod("Add")!;
                        Type valueType = typeArgs[1]; // HashSet<string>
                        object allowedSet = Activator.CreateInstance(valueType)!;
                        valueType.GetMethod("Add")!.Invoke(allowedSet, new object[] { box.Value });
                        addMethod.Invoke(dict, new object[] { box.Key, allowedSet });
                        setMethod.Invoke(null, new object[] { box.Key, dict });
                        Log.LogInfo($"CookingSkillFix: Registered {box.Key} -> {box.Value}");
                    }
                    catch (Exception ex)
                    {
                        Log.LogWarning($"CookingSkillFix: Failed to register {box.Key}: {ex.Message}");
                    }
                }

                // Set stack size to 10 for Valharvest box items in ObjectDB
                // We do this after OdinsFoodBarrels registers so we can find the prefabs
                SetBoxStackSizes(10);
            }
            catch (Exception ex)
            {
                Log.LogError("CookingSkillFix: Error registering Valharvest boxes: " + ex.Message);
            }
        }

        private static void SetBoxStackSizes(int stackSize)
        {
            if (ObjectDB.instance == null) return;
            var items = new[] { "garlic", "pepper", "potato", "tomato", "salt", "apple" };
            foreach (string name in items)
            {
                GameObject? prefab = ObjectDB.instance.GetItemPrefab(name);
                if (prefab == null) continue;
                ItemDrop? drop = prefab.GetComponent<ItemDrop>();
                if (drop == null) continue;
                drop.m_itemData.m_shared.m_maxStackSize = stackSize;
                Log.LogInfo($"CookingSkillFix: Set {name} stack size to {stackSize}");
            }
        }
    }

    internal static class ExtraStations
    {
        // Stations whose recipes should raise Skills.SkillType.Cooking.
        // Smoothbrain's Cooking mod checks m_craftingSkill == Cooking on the station —
        // custom stations from other mods never have that set, so we patch it in ourselves.
        public static readonly HashSet<string> Names = new HashSet<string>
        {
            "rk_griddle",        // Valharvest stone griddle
            "piece_apiary",      // Oh Honey apiary
            "piece_prep_table",  // Valharvest preparation table
        };
    }

    /// <summary>
    /// Fix 1: Set m_craftingSkill = Cooking on custom stations at load time.
    ///
    /// Smoothbrain's mod patches InventoryGui.DoCrafting and checks:
    ///   m_craftRecipe.m_craftingStation.m_craftingSkill == Skills.SkillType.Cooking
    /// If true it multiplies the skill raise by 5x. Our custom stations have
    /// m_craftingSkill = None so they get no cooking XP at all.
    ///
    /// We fix this by patching the station's m_craftingSkill field after ZNetScene
    /// registers all prefabs.
    /// </summary>
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDB_Awake_Patch
    {
        private static void Postfix(ObjectDB __instance)
        {
            // Fix 1: patch custom station craftingSkill
            PatchStationSkills();

            // Fix 2: serving tray compat
            FixServingTray(__instance);
        }

        private static void PatchStationSkills()
        {
            foreach (string stationName in ExtraStations.Names)
            {
                GameObject? prefab = PrefabManager_FindPrefab(stationName);
                if (prefab == null) continue;

                CraftingStation? station = prefab.GetComponent<CraftingStation>();
                if (station == null) continue;

                station.m_craftingSkill = Skills.SkillType.Cooking;
                Plugin.Log.LogInfo($"CookingSkillFix: Set {stationName} craftingSkill = Cooking");
            }
        }

        private static GameObject? PrefabManager_FindPrefab(string name)
        {
            // Try ZNetScene first, then ObjectDB
            if (ZNetScene.instance != null)
            {
                GameObject? go = ZNetScene.instance.GetPrefab(name);
                if (go != null) return go;
            }
            return null;
        }

        private static void FixServingTray(ObjectDB instance)
        {
            int fixedCount = 0;

            foreach (GameObject prefab in instance.m_items)
            {
                if (prefab == null) continue;

                ItemDrop? drop = prefab.GetComponent<ItemDrop>();
                if (drop == null) continue;

                ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;

                if (shared.m_food <= 0f) continue;
                if (shared.m_itemType == ItemDrop.ItemData.ItemType.Material) continue;
                if (!HasVisibleMesh(prefab)) continue;

                string prefabName = ((UnityEngine.Object)prefab).name;
                if (Plugin.VanillaItemNames.Contains(prefabName)) continue;

                Plugin.Log.LogInfo(
                    "CookingSkillFix: Fixing serving tray for " +
                    prefabName + " (type was " + shared.m_itemType + ")"
                );
                shared.m_itemType = ItemDrop.ItemData.ItemType.Material;
                fixedCount++;
            }

            if (fixedCount > 0)
                Plugin.Log.LogInfo("CookingSkillFix: Fixed serving tray compat for " + fixedCount + " items.");
        }

        private static bool HasVisibleMesh(GameObject go)
        {
            foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>(false))
                if (r.enabled) return true;
            foreach (SkinnedMeshRenderer r in go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                if (r.enabled) return true;
            return false;
        }
    }
}

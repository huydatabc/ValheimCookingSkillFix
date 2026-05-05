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

        internal static bool IsModLoaded(string guid)
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

                if (odinAssembly == null) { Log.LogWarning("CookingSkillFix: OdinsFoodBarrels assembly not found."); return; }

                Type? restrictionsType = odinAssembly.GetType("OdinsFoodBarrels.ContainerRestrictions");
                if (restrictionsType == null)
                {
                    foreach (Type t in odinAssembly.GetTypes())
                        if (t.GetMethod("SetContainerRestrictions") != null) { restrictionsType = t; break; }
                }
                if (restrictionsType == null) { Log.LogWarning("CookingSkillFix: ContainerRestrictions type not found."); return; }

                MethodInfo? setMethod = restrictionsType.GetMethod("SetContainerRestrictions",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                if (setMethod == null) { Log.LogWarning("CookingSkillFix: SetContainerRestrictions not found."); return; }

                var parameters = setMethod.GetParameters();
                Type dictType = parameters[1].ParameterType;
                Type[] typeArgs = dictType.GetGenericArguments();

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
                        object dict = Activator.CreateInstance(dictType)!;
                        MethodInfo addMethod = dictType.GetMethod("Add")!;
                        Type valueType = typeArgs[1];
                        object allowedSet = Activator.CreateInstance(valueType)!;
                        valueType.GetMethod("Add")!.Invoke(allowedSet, new object[] { box.Value });
                        addMethod.Invoke(dict, new object[] { box.Key, allowedSet });
                        setMethod.Invoke(null, new object[] { box.Key, dict });
                        Log.LogInfo($"CookingSkillFix: Registered box {box.Key} -> {box.Value}");
                    }
                    catch (Exception ex) { Log.LogWarning($"CookingSkillFix: Failed {box.Key}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { Log.LogError("CookingSkillFix: Box registration error: " + ex.Message); }
        }
    }

    internal static class ExtraStations
    {
        public static readonly HashSet<string> Names = new HashSet<string>
        {
            "rk_griddle",        // Valharvest stone griddle
            "piece_prep_table",  // Valharvest preparation table
                        "piece_apiary",      // Oh Honey apiary
        };
    }

    /// <summary>
    /// Fix 1: Set m_craftingSkill = Cooking on custom stations.
    ///
    /// Smoothbrain's mod checks m_craftingStation.m_craftingSkill == Cooking
    /// in its DoCrafting transpiler. Custom stations never have this set.
    ///
    /// We hook ZNetScene.Awake (fires after Jotunn registers custom prefabs)
    /// to patch the station prefabs directly.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake_Patch
    {
        private static void Postfix(ZNetScene __instance)
        {
            foreach (string stationName in ExtraStations.Names)
            {
                GameObject? prefab = __instance.GetPrefab(stationName);
                if (prefab == null)
                {
                    Plugin.Log.LogWarning($"CookingSkillFix: Prefab {stationName} not found in ZNetScene.");
                    continue;
                }
                CraftingStation? station = prefab.GetComponent<CraftingStation>();
                if (station == null)
                {
                    Plugin.Log.LogWarning($"CookingSkillFix: No CraftingStation component on {stationName}.");
                    continue;
                }
                station.m_craftingSkill = Skills.SkillType.Cooking;
                Plugin.Log.LogInfo($"CookingSkillFix: Set {stationName} m_craftingSkill = Cooking.");
            }
        }
    }

    /// <summary>
    /// Fix 2: Serving tray compatibility.
    ///
    /// The serving tray filters by m_itemType == Material.
    /// BUT Player.EatFood requires m_itemType == Consumable to eat.
    /// So we CANNOT change m_itemType.
    ///
    /// Instead we patch ItemStand.CanAttach (the serving tray's item filter)
    /// to also allow Consumable items that have food stats.
    /// </summary>
    [HarmonyPatch(typeof(ItemStand), "CanAttach")]
    internal static class ItemStand_CanAttach_Patch
    {
        private static void Postfix(ItemStand __instance, ItemDrop.ItemData item, ref bool __result)
        {
            // Only act on the food item stand (serving tray)
            if (__result) return;
            if (!__instance.m_supportedTypes.Contains(ItemDrop.ItemData.ItemType.Material)) return;

            // Allow Consumable food items with a visible prefab
            if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable
                && item.m_shared.m_food > 0f)
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// Fix 3 (ObjectDB): Set stack sizes for box items to 10.
    /// </summary>
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDB_Awake_Patch
    {
        private static void Postfix(ObjectDB __instance)
        {
            if (!Plugin.IsModLoaded("gravebear.odinsfoodbarrels")) return;

            var items = new[] { "garlic", "pepper", "potato", "tomato", "salt", "apple" };
            foreach (string name in items)
            {
                GameObject? prefab = __instance.GetItemPrefab(name);
                if (prefab == null) continue;
                ItemDrop? drop = prefab.GetComponent<ItemDrop>();
                if (drop == null) continue;
                drop.m_itemData.m_shared.m_maxStackSize = 10;
                Plugin.Log.LogInfo($"CookingSkillFix: Set {name} stack size = 10");
            }
        }
    }
}

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

        internal static bool IsModLoaded(string guid)
        {
            foreach (var kv in BepInEx.Bootstrap.Chainloader.PluginInfos)
                if (kv.Key == guid) return true;
            return false;
        }

        private void Awake()
        {
            Log = Logger;
            new Harmony(PluginGUID).PatchAll();
            Log.LogInfo("CookingSkillFix loaded.");
        }

        internal static void RegisterValharvestBoxes()
        {
            try
            {
                Assembly? odinAssembly = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    if (asm.GetName().Name == "OdinsFoodBarrels") { odinAssembly = asm; break; }

                if (odinAssembly == null) { Log.LogWarning("CookingSkillFix: OdinsFoodBarrels assembly not found."); return; }

                Type? restrictionsType = odinAssembly.GetType("OdinsFoodBarrels.RestrictContainers");
                if (restrictionsType == null) { Log.LogWarning("CookingSkillFix: RestrictContainers not found."); return; }

                MethodInfo? setMethod = restrictionsType.GetMethod("SetContainerRestrictions",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                if (setMethod == null) { Log.LogWarning("CookingSkillFix: SetContainerRestrictions not found."); return; }

                FieldInfo? dictField = restrictionsType.GetField("_allowedItemsByContainer",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (dictField == null) { Log.LogWarning("CookingSkillFix: _allowedItemsByContainer not found."); return; }

                var existing = dictField.GetValue(null) as Dictionary<string, HashSet<string>>;
                if (existing == null) { Log.LogWarning("CookingSkillFix: Could not read container restrictions."); return; }

                var boxes = new Dictionary<string, string>
                {
                    { "piece_garlicBox", "garlic" },
                    { "piece_pepperBox", "pepper" },
                    { "piece_potatoBox", "potato" },
                    { "piece_tomatoBox", "tomato" },
                    { "piece_saltBox",   "salt"   },
                    { "piece_appleBox",  "apple"  },
                };

                foreach (var box in boxes)
                    existing["$" + box.Key] = new HashSet<string> { box.Value };

                setMethod.Invoke(null, new object[] { existing });
                Log.LogInfo($"CookingSkillFix: Registered {boxes.Count} Valharvest food boxes.");
            }
            catch (Exception ex) { Log.LogError("CookingSkillFix: Box registration error: " + ex.Message); }
        }
    }

    /// <summary>
    /// Fix 1: Set m_craftingSkill = Cooking on custom stations.
    /// Fix 3: Register Valharvest food boxes with OdinsFoodBarrels.
    /// Both run on ZNetScene.Awake — after all Jotunn prefabs are registered.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake_Patch
    {
        private static void Postfix(ZNetScene __instance)
        {
            // Fix 1
            string[] stations = { "rk_griddle", "piece_prep_table", "piece_apiary" };
            foreach (string name in stations)
            {
                GameObject? prefab = __instance.GetPrefab(name);
                if (prefab == null) { Plugin.Log.LogWarning($"CookingSkillFix: Prefab not found: {name}"); continue; }
                CraftingStation? station = prefab.GetComponent<CraftingStation>();
                if (station == null) { Plugin.Log.LogWarning($"CookingSkillFix: No CraftingStation on: {name}"); continue; }
                station.m_craftingSkill = Skills.SkillType.Cooking;
                Plugin.Log.LogInfo($"CookingSkillFix: Set {name} m_craftingSkill = Cooking.");
            }

            // Fix 3
            if (Plugin.IsModLoaded("gravebear.odinsfoodbarrels"))
                Plugin.RegisterValharvestBoxes();
        }
    }

    /// <summary>
    /// Fix 2: Serving tray compatibility.
    /// Vanilla food uses m_itemType = Material. Mod food uses Consumable, which
    /// the serving tray rejects. We fix mod food items to use Material.
    /// Runs on ObjectDB.CopyOtherDB which fires later than Awake and avoids
    /// the MonoMod.Backports issue triggered by Awake patching.
    /// </summary>
    [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
    internal static class ObjectDB_CopyOtherDB_Patch
    {
        private static readonly HashSet<string> VanillaItems = new HashSet<string>
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

        private static void Postfix(ObjectDB __instance)
        {
            int count = 0;
            foreach (GameObject prefab in __instance.m_items)
            {
                if (prefab == null) continue;
                ItemDrop? drop = prefab.GetComponent<ItemDrop>();
                if (drop == null) continue;
                var shared = drop.m_itemData.m_shared;
                if (shared.m_food <= 0f) continue;
                if (shared.m_itemType == ItemDrop.ItemData.ItemType.Material) continue;
                if (shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable) continue;
                string prefabName = ((UnityEngine.Object)prefab).name;
                if (VanillaItems.Contains(prefabName)) continue;
                shared.m_itemType = ItemDrop.ItemData.ItemType.Material;
                Plugin.Log.LogInfo($"CookingSkillFix: Fixed serving tray type for {prefabName}");
                count++;
            }
            if (count > 0)
                Plugin.Log.LogInfo($"CookingSkillFix: Fixed {count} items for serving tray.");

            // Stack sizes for box items
            if (Plugin.IsModLoaded("gravebear.odinsfoodbarrels"))
            {
                foreach (string name in new[] { "garlic", "pepper", "potato", "tomato", "salt", "apple" })
                {
                    GameObject? prefab = __instance.GetItemPrefab(name);
                    if (prefab == null) continue;
                    ItemDrop? drop = prefab.GetComponent<ItemDrop>();
                    if (drop == null) continue;
                    drop.m_itemData.m_shared.m_maxStackSize = 10;
                }
            }
        }
    }
}

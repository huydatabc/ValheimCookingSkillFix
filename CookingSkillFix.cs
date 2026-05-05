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

            if (IsModLoaded("gravebear.odinsfoodbarrels"))
                RegisterValharvestBoxes();
            else
                Log.LogInfo("CookingSkillFix: OdinsFoodBarrels not found, skipping food box fix.");
        }

        private static void RegisterValharvestBoxes()
        {
            // OdinsFoodBarrels.RestrictContainers.SetContainerRestrictions takes a single
            // Dictionary<string, HashSet<string>> where keys use "$" + prefabName prefix.
            // It REPLACES the whole dict, so we must read the existing one first and merge.
            try
            {
                Assembly? odinAssembly = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    if (asm.GetName().Name == "OdinsFoodBarrels") { odinAssembly = asm; break; }

                if (odinAssembly == null) { Log.LogWarning("CookingSkillFix: OdinsFoodBarrels assembly not found."); return; }

                Type? restrictionsType = odinAssembly.GetType("OdinsFoodBarrels.RestrictContainers");
                if (restrictionsType == null) { Log.LogWarning("CookingSkillFix: RestrictContainers type not found."); return; }

                MethodInfo? setMethod = restrictionsType.GetMethod("SetContainerRestrictions",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                if (setMethod == null) { Log.LogWarning("CookingSkillFix: SetContainerRestrictions not found."); return; }

                // Read the existing _allowedItemsByContainer field so we can merge into it
                FieldInfo? dictField = restrictionsType.GetField("_allowedItemsByContainer",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (dictField == null) { Log.LogWarning("CookingSkillFix: _allowedItemsByContainer field not found."); return; }

                var existing = dictField.GetValue(null) as Dictionary<string, HashSet<string>>;
                if (existing == null) { Log.LogWarning("CookingSkillFix: Could not read existing container restrictions."); return; }

                // Valharvest box → allowed item (stack size 10, matching OdinsFoodBarrels vanilla barrels)
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

                // Pass the merged dict back
                setMethod.Invoke(null, new object[] { existing });
                Log.LogInfo($"CookingSkillFix: Registered {boxes.Count} Valharvest food boxes with OdinsFoodBarrels.");
            }
            catch (Exception ex) { Log.LogError("CookingSkillFix: Box registration error: " + ex.Message); }
        }
    }

    /// <summary>
    /// Fix 1: Set m_craftingSkill = Cooking on custom stations.
    ///
    /// Smoothbrain's IncreaseCraftingSkill transpiler checks:
    ///   m_craftingStation.m_craftingSkill == Skills.SkillType.Cooking
    /// Custom stations from Valharvest/Oh Honey never have this set.
    ///
    /// Valharvest registers prefabs via PrefabManager.OnVanillaPrefabsAvailable
    /// which fires AFTER ZNetScene.Awake. We use Player.Awake as our hook
    /// since by then all custom prefabs are guaranteed to be in ZNetScene.
    /// </summary>
    [HarmonyPatch(typeof(Player), "Awake")]
    internal static class Player_Awake_StationPatch
    {
        private static bool _done = false;

        private static void Postfix()
        {
            if (_done) return;
            _done = true;

            string[] stations = { "rk_griddle", "piece_prep_table", "piece_apiary" };
            foreach (string name in stations)
            {
                if (ZNetScene.instance == null) { Plugin.Log.LogWarning("CookingSkillFix: ZNetScene not ready."); return; }
                GameObject? prefab = ZNetScene.instance.GetPrefab(name);
                if (prefab == null) { Plugin.Log.LogWarning($"CookingSkillFix: Prefab not found: {name}"); continue; }
                CraftingStation? station = prefab.GetComponent<CraftingStation>();
                if (station == null) { Plugin.Log.LogWarning($"CookingSkillFix: No CraftingStation on: {name}"); continue; }
                station.m_craftingSkill = Skills.SkillType.Cooking;
                Plugin.Log.LogInfo($"CookingSkillFix: Set {name} m_craftingSkill = Cooking.");
            }
        }
    }

    /// <summary>
    /// Fix 2: Serving tray compatibility.
    ///
    /// Confirmed from ServeYouRight source: the serving tray requires
    /// m_itemType == Material (value 2). Vanilla food is Material.
    /// Mod food (Valharvest, BoneAppetit, Oh Honey) is incorrectly Consumable.
    ///
    /// We set m_itemType = Material on mod food items at ObjectDB load.
    /// This matches vanilla behaviour and does NOT break eating —
    /// vanilla food is Material and is eatable. The previous eating issue
    /// was caused by something else in an earlier broken build.
    ///
    /// Safety: only items with m_food > 0, a visible mesh, and not in
    /// the vanilla item list are touched.
    /// </summary>
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDB_Awake_Patch
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
            // Fix 2: serving tray item type
            int fixedCount = 0;
            foreach (GameObject prefab in __instance.m_items)
            {
                if (prefab == null) continue;
                ItemDrop? drop = prefab.GetComponent<ItemDrop>();
                if (drop == null) continue;
                var shared = drop.m_itemData.m_shared;
                if (shared.m_food <= 0f) continue;
                if (shared.m_itemType == ItemDrop.ItemData.ItemType.Material) continue;
                if (!HasVisibleMesh(prefab)) continue;
                string prefabName = ((UnityEngine.Object)prefab).name;
                if (VanillaItems.Contains(prefabName)) continue;
                shared.m_itemType = ItemDrop.ItemData.ItemType.Material;
                Plugin.Log.LogInfo($"CookingSkillFix: Fixed serving tray type for {prefabName}");
                fixedCount++;
            }
            if (fixedCount > 0)
                Plugin.Log.LogInfo($"CookingSkillFix: Fixed serving tray for {fixedCount} items.");

            // Box stack sizes (only with OdinsFoodBarrels)
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

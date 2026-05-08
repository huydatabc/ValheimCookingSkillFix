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
    [BepInDependency("com.jotunn.jotunn", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("org.bepinex.plugins.cooking", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("gravebear.odinsfoodbarrels", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "fix.cookingskillfix";
        public const string PluginName = "CookingSkillFix";
        public const string PluginVersion = "1.0.1";

        internal static ManualLogSource Log = null!;

        private void Awake()
        {
            Log = Logger;
            Harmony harmony = new Harmony(PluginGUID);
            harmony.PatchAll();
            Log.LogInfo("CookingSkillFix loaded.");
        }

        internal static bool IsModLoaded(string guid)
        {
            return BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(guid);
        }

        internal static void RegisterValharvestBoxes()
        {
            try
            {
                Assembly odinAssembly = null;

                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "OdinsFoodBarrels")
                    {
                        odinAssembly = asm;
                        break;
                    }
                }

                if (odinAssembly == null)
                {
                    Log.LogWarning("OdinsFoodBarrels assembly not found.");
                    return;
                }

                Type restrictionsType = odinAssembly.GetType("OdinsFoodBarrels.RestrictContainers");

                if (restrictionsType == null)
                {
                    Log.LogWarning("RestrictContainers type not found.");
                    return;
                }

                MethodInfo setMethod = restrictionsType.GetMethod(
                    "SetContainerRestrictions",
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Static
                );

                FieldInfo dictField = restrictionsType.GetField(
                    "_allowedItemsByContainer",
                    BindingFlags.NonPublic |
                    BindingFlags.Static
                );

                if (setMethod == null || dictField == null)
                {
                    Log.LogWarning("OdinsFoodBarrels reflection failed.");
                    return;
                }

                var existing = dictField.GetValue(null) as Dictionary<string, HashSet<string>>;

                if (existing == null)
                {
                    Log.LogWarning("Could not read restriction dictionary.");
                    return;
                }

                Dictionary<string, string> boxes = new Dictionary<string, string>
                {
                    { "piece_garlicBox", "garlic" },
                    { "piece_pepperBox", "pepper" },
                    { "piece_potatoBox", "potato" },
                    { "piece_tomatoBox", "tomato" },
                    { "piece_saltBox", "salt" },
                    { "piece_appleBox", "apple" }
                };

                foreach (KeyValuePair<string, string> kv in boxes)
                {
                    existing[kv.Key] = new HashSet<string> { kv.Value };
                }

                setMethod.Invoke(null, new object[] { existing });

                Log.LogInfo($"Registered {boxes.Count} Valharvest food boxes.");
            }
            catch (Exception ex)
            {
                Log.LogError($"RegisterValharvestBoxes error: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    public static class ObjectDBAwakePatch
    {
        private static void Postfix(ObjectDB __instance)
        {
            FoodFixer.Fix(__instance);
        }
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    public static class ObjectDBCopyPatch
    {
        private static void Postfix(ObjectDB __instance)
        {
            FoodFixer.Fix(__instance);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    public static class ZNetScenePatch
    {
        private static void Postfix(ZNetScene __instance)
        {
            StationFixer.Fix(__instance);
        }
    }

    public static class FoodFixer
    {
        private static readonly HashSet<string> VanillaItems = new HashSet<string>
        {
            "Blueberries","Raspberry","Cloudberry","Carrot","Turnip","Onion",
            "Barley","BarleyFlour","Flax","Mushroom","MushroomBlue",
            "MushroomYellow","Thistle","Dandelion","Honey","RoyalJelly"
        };

        public static void Fix(ObjectDB objectDb)
        {
            try
            {
                if (objectDb == null || objectDb.m_items == null)
                    return;

                int fixedCount = 0;

                foreach (GameObject itemPrefab in objectDb.m_items)
                {
                    if (itemPrefab == null)
                        continue;

                    ItemDrop drop = itemPrefab.GetComponent<ItemDrop>();

                    if (drop == null)
                        continue;

                    ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;

                    if (shared == null)
                        continue;

                    if (shared.m_food <= 0f)
                        continue;

                    if (shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable)
                        continue;

                    if (VanillaItems.Contains(itemPrefab.name))
                        continue;

                    MeshRenderer renderer = itemPrefab.GetComponentInChildren<MeshRenderer>();

                    if (renderer == null)
                        continue;

                    shared.m_itemType = ItemDrop.ItemData.ItemType.Material;

                    fixedCount++;

                    Plugin.Log.LogInfo($"Fixed food item type: {itemPrefab.name}");
                }

                if (fixedCount > 0)
                {
                    Plugin.Log.LogInfo($"Fixed {fixedCount} modded food items.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"FoodFixer error: {ex}");
            }
        }
    }

    public static class StationFixer
    {
        private static bool _done;

        public static void Fix(ZNetScene scene)
        {
            try
            {
                if (_done || scene == null)
                    return;

                string[] stations =
                {
                    "rk_griddle",
                    "piece_prep_table",
                    "piece_apiary"
                };

                bool success = false;

                foreach (string name in stations)
                {
                    GameObject prefab = scene.GetPrefab(name);

                    if (prefab == null)
                    {
                        Plugin.Log.LogWarning($"Prefab not found: {name}");
                        continue;
                    }

                    CraftingStation station = prefab.GetComponent<CraftingStation>();

                    if (station == null)
                    {
                        Plugin.Log.LogWarning($"No CraftingStation on: {name}");
                        continue;
                    }

                    station.m_craftingSkill = Skills.SkillType.Cooking;

                    Plugin.Log.LogInfo($"Set crafting skill Cooking on {name}");

                    success = true;
                }

                if (Plugin.IsModLoaded("gravebear.odinsfoodbarrels"))
                {
                    Plugin.RegisterValharvestBoxes();
                }

                if (success)
                {
                    _done = true;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"StationFixer error: {ex}");
            }
        }
    }
}

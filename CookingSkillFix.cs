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

                Type pluginType =
                    odinAssembly.GetType("OdinsFoodBarrels.OdinsFoodBarrelsPlugin");

                Type restrictType =
                    odinAssembly.GetType("OdinsFoodBarrels.RestrictContainers");

                if (pluginType == null || restrictType == null)
                {
                    Log.LogWarning("Odin types not found.");
                    return;
                }

                FieldInfo dictField = pluginType.GetField(
                    "ContainerRestrictions",
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Static
                );

                MethodInfo setMethod = restrictType.GetMethod(
                    "SetContainerRestrictions",
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Static
                );

                if (dictField == null || setMethod == null)
                {
                    Log.LogWarning("Reflection targets missing.");
                    return;
                }

                var dict =
                    dictField.GetValue(null)
                    as Dictionary<string, HashSet<string>>;

                if (dict == null)
                {
                    Log.LogWarning("ContainerRestrictions is null.");
                    return;
                }

                Dictionary<string, string> boxes =
                    new Dictionary<string, string>
                {
                    { "piece_garlicBox", "Garlic" },
                    { "piece_pepperBox", "Pepper" },
                    { "piece_potatoBox", "Potato" },
                    { "piece_tomatoBox", "Tomato" },
                    { "piece_saltBox", "Salt" },
                    { "piece_appleBox", "Apple" }
                };

                foreach (var kv in boxes)
                {
                    // -------------------------
                    // Odin restriction fix
                    // -------------------------
                    dict[kv.Value] = new HashSet<string> { kv.Value };

                    // -------------------------
                    // Prefab / container UI
                    // -------------------------
                    GameObject prefab = ZNetScene.instance?.GetPrefab(kv.Key);

                    if (prefab == null)
                    {
                        Log.LogWarning($"Prefab not found: {kv.Key}");
                        continue;
                    }

                    Container container = prefab.GetComponent<Container>();

                    if (container == null)
                    {
                        GameObject chestPrefab =
                            ZNetScene.instance.GetPrefab("piece_chest");

                        if (chestPrefab != null)
                        {
                            Container chestContainer =
                                chestPrefab.GetComponent<Container>();

                            if (chestContainer != null)
                            {
                                container = prefab.AddComponent<Container>();

                                container.m_width = 6;
                                container.m_height = 2;
                                container.m_checkGuardStone = false;

                                Log.LogInfo($"Added Container component to {kv.Key}");
                            }
                        }
                        else
                        {
                            Log.LogWarning("piece_chest prefab not found");
                        }
                    }

                    if (container != null)
                    {
                        container.m_name = $"{kv.Value} Box";
                    }

                    // -------------------------
                    // Recipe fix (ObjectDB)
                    // -------------------------
                    if (ObjectDB.instance != null && ObjectDB.instance.m_recipes != null)
                    {
                        foreach (Recipe recipe in ObjectDB.instance.m_recipes)
                        {
                            if (recipe?.m_item == null)
                                continue;

                            if (recipe.m_item.name != kv.Key)
                                continue;

                            if (recipe.m_resources == null)
                                continue;

                            foreach (Piece.Requirement req in recipe.m_resources)
                            {
                                if (req?.m_resItem == null)
                                    continue;

                                if (req.m_resItem.name == "Wood")
                                    req.m_amount = 1;
                                else
                                    req.m_amount = 10;

                                req.m_recover = true;

                                Log.LogInfo(
                                    $"Recipe patched: {kv.Key} {req.m_resItem.name} -> {req.m_amount}"
                                );
                            }
                        }
                    }

                    Log.LogInfo(
                        $"Registered Odin container: {kv.Key} -> {kv.Value}"
                    );
                }

                setMethod.Invoke(null, new object[] { dict });

                Log.LogInfo("Valharvest Odin integration complete.");
            }
            catch (Exception ex)
            {
                Log.LogError(ex);
            }
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

    [HarmonyPatch(typeof(InventoryGui), "OnCraftPressed")]
    public static class CookingXpPatch
    {
        private static void Prefix(InventoryGui __instance)
        {
            try
            {
                CraftingStation station = Player.m_localPlayer?
                    .GetCurrentCraftingStation();

                if (station == null)
                    return;

                string name = station.gameObject.name;

                Plugin.Log.LogInfo($"Craft station: {name}");

                if (
                    name.Contains("piece_prep_table") ||
                    name.Contains("piece_cooking_pot")
                )
                {
                    Player.m_localPlayer.RaiseSkill(
                        Skills.SkillType.Cooking,
                        1f
                    );

                    Plugin.Log.LogInfo(
                        $"Granted cooking XP at {name}"
                    );
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(ex);
            }
        }
    }
}

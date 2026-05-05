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

        private void Awake()
        {
            Log = Logger;
            new Harmony(PluginGUID).PatchAll();
            Log.LogInfo("CookingSkillFix loaded.");

            // Fix 3: register Valharvest food boxes as OdinsFoodBarrels containers
            // Must run after both mods have initialised, so we defer to next frame.
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

        /// <summary>
        /// Registers Valharvest's buildable food boxes with OdinsFoodBarrels'
        /// ContainerRestrictions system so they behave like barrels — interact
        /// to take items rather than build/deconstruct.
        ///
        /// Calls OdinsFoodBarrels.ContainerRestrictions.SetContainerRestrictions
        /// via reflection since we don't have a direct reference to the assembly.
        ///
        /// Box → allowed item mapping (from Valharvest.dll strings):
        ///   piece_garlicBox  → garlic
        ///   piece_pepperBox  → pepper
        ///   piece_potatoBox  → potato
        ///   piece_tomatoBox  → tomato
        ///   piece_saltBox    → salt
        ///   piece_appleBox   → apple
        /// </summary>
        private static void RegisterValharvestBoxes()
        {
            try
            {
                // Find OdinsFoodBarrels assembly
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

                // Find ContainerRestrictions type
                Type? restrictionsType = odinAssembly.GetType("OdinsFoodBarrels.ContainerRestrictions");
                if (restrictionsType == null)
                {
                    // Try searching all types for the one with SetContainerRestrictions
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
                    Log.LogWarning("CookingSkillFix: Could not find ContainerRestrictions type in OdinsFoodBarrels.");
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

                // Map of Valharvest box piece name -> allowed item prefab name
                var boxes = new Dictionary<string, string>
                {
                    { "piece_garlicBox", "garlic"  },
                    { "piece_pepperBox", "pepper"  },
                    { "piece_potatoBox", "potato"  },
                    { "piece_tomatoBox", "tomato"  },
                    { "piece_saltBox",   "salt"    },
                    { "piece_appleBox",  "apple"   },
                };

                // Check what parameter types SetContainerRestrictions expects
                var parameters = setMethod.GetParameters();
                Log.LogInfo("CookingSkillFix: SetContainerRestrictions params: " +
                    string.Join(", ", Array.ConvertAll(parameters, p => p.ParameterType.Name)));

                foreach (var box in boxes)
                {
                    try
                    {
                        // Try calling with (string containerName, List<string> allowedItems)
                        // The allowed items list contains the single item this box holds
                        object allowedItems;
                        if (parameters.Length >= 2 && parameters[1].ParameterType == typeof(List<string>))
                            allowedItems = new List<string> { box.Value };
                        else if (parameters.Length >= 2 && parameters[1].ParameterType == typeof(string[]))
                            allowedItems = new string[] { box.Value };
                        else
                            allowedItems = new List<string> { box.Value };

                        setMethod.Invoke(null, new object[] { box.Key, allowedItems });
                        Log.LogInfo($"CookingSkillFix: Registered {box.Key} -> {box.Value}");
                    }
                    catch (Exception ex)
                    {
                        Log.LogWarning($"CookingSkillFix: Failed to register {box.Key}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogError("CookingSkillFix: Error registering Valharvest boxes: " + ex.Message);
            }
        }
    }

    internal static class ExtraStations
    {
        // rk_oven intentionally excluded — vanilla now has its own oven.
        public static readonly HashSet<string> Names = new HashSet<string>
        {
            "rk_griddle",        // Valharvest stone griddle
            "piece_apiary",      // Oh Honey apiary
            "piece_prep_table",  // Valharvest preparation table
        };
    }

    /// <summary>
    /// Fix 1: Raises Smoothbrain's Cooking skill when crafting at a custom
    /// cooking station that his mod doesn't know about.
    /// </summary>
    [HarmonyPatch(typeof(Player), "ConsumeResources")]
    internal static class ConsumeResources_Patch
    {
        private static Skills.SkillType? _cookingSkillType = null;
        private static bool _lookupFailed = false;

        private static void Postfix(Player __instance)
        {
            if (_lookupFailed) return;

            CraftingStation? station = __instance.GetCurrentCraftingStation();
            if (station == null) return;

            string stationName = ((UnityEngine.Object)station).name
                .Replace("(Clone)", "")
                .Trim();

            if (!ExtraStations.Names.Contains(stationName)) return;

            if (_cookingSkillType == null)
            {
                _cookingSkillType = FindCookingSkillType(__instance);
                if (_cookingSkillType == null)
                {
                    _lookupFailed = true;
                    Plugin.Log.LogWarning("CookingSkillFix: Could not find Cooking skill — is Smoothbrain's Cooking mod installed?");
                    return;
                }
                Plugin.Log.LogInfo("CookingSkillFix: Found Cooking skill type = " + (int)_cookingSkillType.Value);
            }

            __instance.RaiseSkill(_cookingSkillType.Value, 1f);
        }

        private static Skills.SkillType? FindCookingSkillType(Player player)
        {
            Skills? skills = player?.GetSkills();
            if (skills == null) return null;

            FieldInfo? field = typeof(Skills).GetField("m_skills", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) return null;

            var defs = field.GetValue(skills) as List<Skills.SkillDef>;
            if (defs == null) return null;

            foreach (Skills.SkillDef def in defs)
            {
                int id = (int)def.m_skill;
                if (id <= 100) continue;
                string desc = def.m_description ?? "";
                if (desc.IndexOf("cook", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Plugin.Log.LogInfo("CookingSkillFix: Matched cooking skill: id=" + id + " desc=" + desc);
                    return def.m_skill;
                }
            }

            foreach (Skills.SkillDef def in defs)
            {
                if ((int)def.m_skill > 100)
                {
                    Plugin.Log.LogWarning("CookingSkillFix: Falling back to first custom skill id=" + (int)def.m_skill);
                    return def.m_skill;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Fix 2: Serving tray compatibility.
    /// Fixes m_itemType on any food item with a visible mesh so the vanilla
    /// serving tray accepts it.
    /// </summary>
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDB_Awake_Patch
    {
        private static void Postfix(ObjectDB __instance)
        {
            int fixedCount = 0;

            foreach (GameObject prefab in __instance.m_items)
            {
                if (prefab == null) continue;

                ItemDrop? drop = prefab.GetComponent<ItemDrop>();
                if (drop == null) continue;

                ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;

                if (shared.m_food <= 0f) continue;
                if (shared.m_itemType == ItemDrop.ItemData.ItemType.Material) continue;
                if (!HasVisibleMesh(prefab)) continue;

                Plugin.Log.LogInfo(
                    "CookingSkillFix: Fixing serving tray for " +
                    ((UnityEngine.Object)prefab).name + " (type was " + shared.m_itemType + ")"
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

    /// <summary>
    /// Fix 4: Remove Valharvest's rk_oven from the buildable pieces list.
    /// Vanilla now has its own oven (piece_oven), making rk_oven redundant.
    /// We hook ZNetScene.Awake which fires after all pieces are registered,
    /// and remove it from every PieceTable so it no longer appears in the
    /// hammer build menu.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake_Patch
    {
        private static void Postfix()
        {
            const string ovenPrefab = "rk_oven";
            int removedFrom = 0;

            foreach (PieceTable table in Resources.FindObjectsOfTypeAll<PieceTable>())
            {
                for (int i = table.m_pieces.Count - 1; i >= 0; i--)
                {
                    if (table.m_pieces[i] == null) continue;
                    if (((UnityEngine.Object)table.m_pieces[i]).name
                        .Replace("(Clone)", "").Trim() == ovenPrefab)
                    {
                        table.m_pieces.RemoveAt(i);
                        removedFrom++;
                    }
                }
            }

            if (removedFrom > 0)
                Plugin.Log.LogInfo($"CookingSkillFix: Removed {ovenPrefab} from {removedFrom} piece table(s).");
            else
                Plugin.Log.LogInfo($"CookingSkillFix: {ovenPrefab} not found in any piece table (may not be installed).");
        }
    }
}

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

        // Vanilla item names that should never be touched by Fix 2.
        // These are already Consumable type intentionally and the serving tray
        // handles them fine without us changing their type.
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

                var boxes = new Dictionary<string, string>
                {
                    { "piece_garlicBox", "garlic"  },
                    { "piece_pepperBox", "pepper"  },
                    { "piece_potatoBox", "potato"  },
                    { "piece_tomatoBox", "tomato"  },
                    { "piece_saltBox",   "salt"    },
                    { "piece_appleBox",  "apple"   },
                };

                var parameters = setMethod.GetParameters();
                Log.LogInfo("CookingSkillFix: SetContainerRestrictions params: " +
                    string.Join(", ", Array.ConvertAll(parameters, p => p.ParameterType.Name)));

                foreach (var box in boxes)
                {
                    try
                    {
                        // SetContainerRestrictions takes (string containerName, Dictionary<string, int> allowedItems)
                        // where the dict maps item prefab name -> stack size
                        object allowedItems;
                        if (parameters.Length >= 2 && parameters[1].ParameterType == typeof(Dictionary<string, int>))
                            allowedItems = new Dictionary<string, int> { { box.Value, 50 } };
                        else if (parameters.Length >= 2 && parameters[1].ParameterType == typeof(List<string>))
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
    /// Only touches items with m_food > 0, a visible mesh, AND that aren't
    /// vanilla items (which are already handled correctly by the game).
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

                string prefabName = ((UnityEngine.Object)prefab).name;

                // Skip vanilla items — they work fine already
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

    /// <summary>
    /// Fix 4: Remove Valharvest's rk_oven from the buildable pieces list.
    /// Hooks Piece.Awake instead of ZNetScene.Awake to avoid the
    /// MonoMod.Backports crash. Removes rk_oven from its piece table
    /// the first time any piece is instantiated (safe late hook point).
    /// </summary>
    [HarmonyPatch(typeof(FejdStartup), "Awake")]
    internal static class FejdStartup_Awake_Patch
    {
        private static bool _done = false;

        private static void Postfix()
        {
            if (_done) return;
            _done = true;
            RemoveOven();
        }

        private static void RemoveOven()
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
                Plugin.Log.LogInfo($"CookingSkillFix: {ovenPrefab} not found in any piece table.");
        }
    }
}

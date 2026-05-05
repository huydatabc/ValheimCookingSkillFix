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
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID    = "fix.cookingskillfix";
        public const string PluginName    = "CookingSkillFix";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            new Harmony(PluginGUID).PatchAll();
            Log.LogInfo("CookingSkillFix loaded.");
        }
    }

    internal static class ExtraStations
    {
        public static readonly HashSet<string> Names = new HashSet<string>
        {
            "rk_griddle",        // Valharvest stone griddle
            "piece_apiary",      // Oh Honey apiary
            "piece_prep_table",  // Valharvest preparation table
        };
    }

    // ConsumeResources is called by Player during every craft, and Valharvest confirms
    // it exists. We use it as our hook point since Player.Craft/CraftItem naming varies
    // between publicized assembly versions.
    [HarmonyPatch(typeof(Player), "ConsumeResources")]
    internal static class ConsumeResources_Patch
    {
        private static Skills.SkillType? _cookingSkillType = null;
        private static bool _lookupFailed = false;

        private static void Postfix(Player __instance)
        {
            if (_lookupFailed) return;

            CraftingStation station = __instance.GetCurrentCraftingStation();
            if (station == null) return;

            string stationName = ((Object)station).name
                .Replace("(Clone)", "")
                .Trim();

            if (!ExtraStations.Names.Contains(stationName)) return;

            if (_cookingSkillType == null)
            {
                _cookingSkillType = FindCookingSkillType(__instance);
                if (_cookingSkillType == null)
                {
                    _lookupFailed = true;
                    Plugin.Log.LogWarning("CookingSkillFix: Could not find blaxxun's Cooking skill — is the Cooking mod installed?");
                    return;
                }
                Plugin.Log.LogInfo("CookingSkillFix: Found Cooking skill type = " + (int)_cookingSkillType.Value);
            }

            __instance.RaiseSkill(_cookingSkillType.Value, 1f);
        }

        private static Skills.SkillType? FindCookingSkillType(Player player)
        {
            Skills skills = player?.GetSkills();
            if (skills == null) return null;

            FieldInfo field = typeof(Skills).GetField("m_skills", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) return null;

            var defs = field.GetValue(skills) as List<Skills.SkillDef>;
            if (defs == null) return null;

            foreach (Skills.SkillDef def in defs)
            {
                int id = (int)def.m_skill;
                if (id <= 100) continue; // skip vanilla skills

                string desc = def.m_description ?? "";
                if (desc.IndexOf("cook", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Plugin.Log.LogInfo("CookingSkillFix: Matched cooking skill: id=" + id + " desc=" + desc);
                    return def.m_skill;
                }
            }

            // Fallback: first custom skill if description match fails
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
}

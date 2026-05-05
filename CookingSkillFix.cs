using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CookingSkillFix;

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
        Log.LogInfo("CookingSkillFix loaded — patching rk_griddle and piece_apiary into cooking skill.");
    }
}

/// <summary>
/// These are the custom station prefab names we want treated as cooking stations.
/// rk_griddle   = Valharvest stone griddle
/// piece_apiary = Oh Honey apiary
/// </summary>
internal static class ExtraStations
{
    public static readonly HashSet<string> Names = new HashSet<string>
    {
        "rk_griddle",
        "piece_apiary",
    };
}

/// <summary>
/// Harmony patch on Player.Craft.
///
/// How blaxxun's Cooking mod works (inferred from community knowledge):
///   It patches Player.Craft and checks whether the current crafting station
///   is a known cooking station. If yes, it raises his custom "Cooking" skill
///   instead of the vanilla SkillType that the item's SharedData would give.
///   He identifies his skill by a custom SkillType value registered at startup.
///
/// Our approach:
///   We run a Postfix AFTER Player.Craft completes. At that point vanilla has
///   already raised whatever skill the recipe declared (likely Crafting, since
///   rk_griddle/piece_apiary items have no explicit skillType set).
///   We check if the station is one of our extra stations, and if blaxxun's
///   Cooking skill is registered in the Skills system. If both are true, we
///   raise that skill by the same flat amount blaxxun uses (1f), and undo the
///   incorrect Crafting XP by lowering it back the same amount.
///
///   If blaxxun's mod is absent we do nothing — cooking skill won't exist.
/// </summary>
[HarmonyPatch(typeof(Player), nameof(Player.Craft))]
internal static class Player_Craft_Patch
{
    // Cached once after first successful lookup so we're not searching every craft.
    private static Skills.SkillType? _cookingSkillType = null;
    private static bool _lookupFailed = false;

    private static void Postfix(Player __instance, Recipe recipe, int qualityLevel, bool fromItem, ItemDrop.ItemData item)
    {
        if (_lookupFailed) return;

        // Only act on our extra stations
        CraftingStation station = Player.m_localPlayer?.GetCurrentCraftingStation();
        if (station == null) return;

        string stationName = ((Object)station).name
            .Replace("(Clone)", "")
            .Trim();

        if (!ExtraStations.Names.Contains(stationName)) return;

        // Try to find blaxxun's Cooking skill type (only search once)
        if (_cookingSkillType == null)
        {
            _cookingSkillType = FindCookingSkillType(__instance);
            if (_cookingSkillType == null)
            {
                _lookupFailed = true;
                Plugin.Log.LogWarning("CookingSkillFix: Could not find blaxxun's Cooking skill — is the Cooking mod installed?");
                return;
            }
            Plugin.Log.LogInfo($"CookingSkillFix: Found Cooking skill type = {(int)_cookingSkillType.Value}");
        }

        // Raise cooking skill
        __instance.RaiseSkill(_cookingSkillType.Value, 1f);

        // Undo the incorrect Crafting XP that vanilla awarded (best effort — lower by same amount)
        // We find what skill the recipe item declared and lower it.
        if (recipe?.m_item?.m_itemData?.m_shared != null)
        {
            Skills.SkillType wrongSkill = recipe.m_item.m_itemData.m_shared.m_skillType;
            if (wrongSkill != Skills.SkillType.None && wrongSkill != _cookingSkillType.Value)
            {
                __instance.GetSkills().GetSkillData(wrongSkill)?.m_accumulator.SetValue(
                    Mathf.Max(0f, __instance.GetSkills().GetSkillData(wrongSkill)?.m_accumulator ?? 0f - 1f)
                );
            }
        }
    }

    /// <summary>
    /// Walk all registered skill definitions and find the one named "Cooking".
    /// blaxxun registers his skill with the display name "$skill_cooking" or "Cooking".
    /// The SkillType int value is dynamic — assigned at runtime — so we can't hardcode it.
    /// </summary>
    private static Skills.SkillType? FindCookingSkillType(Player player)
    {
        Skills skills = player?.GetSkills();
        if (skills == null) return null;

        // Skills.m_skills is a List<Skills.SkillDef>
        FieldInfo fieldInfo = typeof(Skills).GetField("m_skills", BindingFlags.NonPublic | BindingFlags.Instance);
        if (fieldInfo == null) return null;

        var skillDefs = fieldInfo.GetValue(skills) as List<Skills.SkillDef>;
        if (skillDefs == null) return null;

        foreach (Skills.SkillDef def in skillDefs)
        {
            // blaxxun's skill token is "$skill_cooking"
            if (def.m_skill.ToString().Equals("Cooking", System.StringComparison.OrdinalIgnoreCase)
                || def.m_description?.Equals("$skill_cooking", System.StringComparison.OrdinalIgnoreCase) == true
                || (int)def.m_skill > 100) // custom skills always have IDs > vanilla range
            {
                // Validate it's actually the cooking skill by checking description token
                Plugin.Log.LogInfo($"CookingSkillFix: Candidate skill — type={def.m_skill}, desc={def.m_description}");
                if (def.m_description?.Contains("cook") == true ||
                    def.m_description?.Contains("Cook") == true ||
                    def.m_description?.Contains("$skill_cooking") == true)
                {
                    return def.m_skill;
                }
            }
        }

        // Fallback: just grab the first custom skill (ID > 100) if description matching fails
        foreach (Skills.SkillDef def in skillDefs)
        {
            if ((int)def.m_skill > 100)
            {
                Plugin.Log.LogWarning($"CookingSkillFix: Falling back to first custom skill type={def.m_skill}");
                return def.m_skill;
            }
        }

        return null;
    }
}

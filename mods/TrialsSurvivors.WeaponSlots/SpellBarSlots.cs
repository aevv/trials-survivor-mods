using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TrialsSurvivors.WeaponSlots;

[HarmonyPatch(typeof(UI_Module_SpellBar), nameof(UI_Module_SpellBar.OnInitialize))]
internal static class SpellBarInitializePatch
{
    [HarmonyPrefix]
    private static void Prefix(UI_Module_SpellBar __instance)
    {
        SpellBarSlots.Current = __instance;
        try
        {
            SpellBarSlots.Ensure(__instance, Plugin.Instance.Slots);
            SpellBarAligner.Schedule(__instance);
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't add spell bar slots: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(UI_Module_SpellBar), nameof(UI_Module_SpellBar.OnSkillAdded))]
internal static class SpellBarSkillAddedPatch
{
    [HarmonyPostfix]
    private static void Postfix(UI_Module_SpellBar __instance, SSV2_SkillInstance skill)
    {
        try
        {
            var module = __instance._skillsModule;
            if (module == null || skill == null) return;

            var slot = module.GetSkillSlotIndex(skill.ID);
            var uiSlots = __instance._spellSlots?.Length ?? 0;
            Plugin.Instance.Log.LogInfo(
                $"weapon '{skill.name}' went into slot {slot + 1}/{module._skillSlots.Length} ({module.Skills.Count} equipped, {uiSlots} spell bar slots)");
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't log added weapon: {e.Message}");
        }
    }
}

internal static class SpellBarSlots
{
    public static UI_Module_SpellBar? Current;

    public static void Ensure(UI_Module_SpellBar bar, int wanted)
    {
        var slots = bar.GetComponentsInChildren<UI_Module_SpellSlot>(true);
        if (slots.Length >= wanted) return;
        if (slots.Length == 0)
        {
            Plugin.Instance.Log.LogWarning($"spell bar '{bar.name}' has no slots to copy");
            return;
        }

        var last = slots[slots.Length - 1];
        var lastRect = last.transform.Cast<RectTransform>();
        var parent = lastRect.parent;
        var layout = parent.GetComponent<LayoutGroup>();
        var spacing = Vector2.zero;
        if (layout == null && slots.Length >= 2)
        {
            var previous = slots[slots.Length - 2].transform.Cast<RectTransform>();
            if (previous.parent == parent) spacing = lastRect.anchoredPosition - previous.anchoredPosition;
        }

        var active = 0;
        foreach (var slot in slots)
        {
            if (slot.gameObject.activeSelf) active++;
        }

        for (var index = slots.Length; index < wanted; index++)
        {
            var clone = Object.Instantiate(last.gameObject, parent);
            clone.name = $"{last.gameObject.name} (WeaponSlots {index + 1})";
            var cloneRect = clone.transform.Cast<RectTransform>();
            cloneRect.SetSiblingIndex(lastRect.GetSiblingIndex() + 1);
            if (layout == null) cloneRect.anchoredPosition = lastRect.anchoredPosition + spacing;

            last = clone.GetComponent<UI_Module_SpellSlot>();
            lastRect = cloneRect;
        }

        Plugin.Instance.Log.LogInfo(
            $"spell bar '{bar.name}': had {slots.Length} slots ({active} active) under '{parent.name}', " +
            $"layout {(layout != null ? layout.GetIl2CppType().Name : $"none, spaced {spacing}")}; now {wanted}");
    }
}

using System;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace TrialsSurvivors.WeaponSlots;

[HarmonyPatch(typeof(ARPGEntity_Module_Skills), nameof(ARPGEntity_Module_Skills.OnInitializeModule))]
internal static class WidenSlotArrayPatch
{
    private static bool _logged;

    [HarmonyPostfix]
    private static void Postfix(ARPGEntity_Module_Skills __instance)
    {
        try
        {
            var slots = __instance._skillSlots;
            var wanted = Plugin.Instance.Slots;
            if (slots == null || slots.Length >= wanted) return;

            var widened = new Il2CppStructArray<int>(wanted);
            for (var i = 0; i < wanted; i++) widened[i] = i < slots.Length ? slots[i] : -1;
            __instance._skillSlots = widened;

            if (_logged && !Plugin.Instance.Verbose.Value) return;
            _logged = true;
            Plugin.Instance.Log.LogInfo($"widened skill slots {slots.Length} -> {wanted} on '{__instance.name}'");
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't widen skill slots: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(ARPGEntity_Module_Skills), nameof(ARPGEntity_Module_Skills.FindNextSelectableSlot))]
internal static class FindNextSelectableSlotPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ARPGEntity_Module_Skills __instance, int currentSlotIndex, ref int __result) =>
        SlotCycling.TryStep(__instance, currentSlotIndex, 1, ref __result);
}

[HarmonyPatch(typeof(ARPGEntity_Module_Skills), nameof(ARPGEntity_Module_Skills.FindPreviousSelectableSlot))]
internal static class FindPreviousSelectableSlotPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ARPGEntity_Module_Skills __instance, int currentSlotIndex, ref int __result) =>
        SlotCycling.TryStep(__instance, currentSlotIndex, -1, ref __result);
}

[HarmonyPatch(typeof(ARPGEntity_Module_Skills), nameof(ARPGEntity_Module_Skills.GetNextSelectableSkillExcluding))]
internal static class GetNextSelectableSkillExcludingPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ARPGEntity_Module_Skills __instance, int excludeSkillId, ref SSV2_SkillInstance? __result) =>
        SlotCycling.TryNextExcluding(__instance, excludeSkillId, ref __result);
}

internal static class SlotCycling
{
    public static bool TryStep(ARPGEntity_Module_Skills module, int current, int direction, ref int result)
    {
        try
        {
            result = Step(module, current, direction);
            if (Plugin.Instance.Verbose.Value) Plugin.Instance.Log.LogInfo($"cycle {(direction > 0 ? "next" : "previous")}: slot {current} -> {result}");
            return false;
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"slot cycling failed, falling back to the game's 5-slot version: {e.Message}");
            return true;
        }
    }

    public static bool TryNextExcluding(ARPGEntity_Module_Skills module, int excludeSkillId, ref SSV2_SkillInstance? result)
    {
        try
        {
            result = NextExcluding(module, excludeSkillId);
            return false;
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"next-skill lookup failed, falling back to the game's 5-slot version: {e.Message}");
            return true;
        }
    }

    private static int Step(ARPGEntity_Module_Skills module, int current, int direction)
    {
        var count = module._skillSlots.Length;
        if (current == -1)
        {
            for (var i = 0; i < count; i++)
            {
                var slot = direction > 0 ? i : count - 1 - i;
                if (Selectable(module, slot, out _)) return slot;
            }
            return -1;
        }

        for (var step = 1; step < count; step++)
        {
            var slot = Wrap(current + direction * step, count);
            if (Selectable(module, slot, out _)) return slot;
        }
        return current;
    }

    private static SSV2_SkillInstance? NextExcluding(ARPGEntity_Module_Skills module, int excludeSkillId)
    {
        var count = module._skillSlots.Length;
        var current = module._currentSkillSlotIndex;
        var first = current == -1 ? 0 : 1;

        for (var step = first; step < count; step++)
        {
            var slot = current == -1 ? step : Wrap(current + step, count);
            if (Selectable(module, slot, out var skill) && skill!.ID != excludeSkillId) return skill;
        }
        return null;
    }

    private static int Wrap(int slot, int count) => (slot % count + count) % count;

    private static bool Selectable(ARPGEntity_Module_Skills module, int slot, out SSV2_SkillInstance? skill)
    {
        skill = null;
        var slots = module._skillSlots;
        if (slot < 0 || slot >= slots.Length) return false;

        var id = slots[slot];
        if (id == -1) return false;

        return module.Skills.TryGetValue(id, out skill) && skill != null && skill._isSelectable;
    }
}

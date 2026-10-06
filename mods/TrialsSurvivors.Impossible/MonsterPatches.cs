using System;
using System.Collections.Generic;
using HarmonyLib;

namespace TrialsSurvivors.Impossible;

[HarmonyPatch(typeof(ARPGEntity_Module_DifficultyScaling), nameof(ARPGEntity_Module_DifficultyScaling.ApplyEntry))]
internal static class ScalingApplyPatch
{
    internal static readonly Dictionary<IntPtr, float> Deltas = new();

    [HarmonyPostfix]
    private static void Postfix(ARPGEntity_Module_DifficultyScaling __instance, int tier)
    {
        try
        {
            if (!ImpossibleState.IsActive) return;
            var entity = __instance.ARPGEntity;
            if (entity == null || entity.Faction == SS_FactionsDATA.PLAYER) return;

            var key = __instance.Pointer;
            ScalingRevertPatch.Revert(__instance, key);

            var stat = ImpossibleState.MaxHealthStat(entity);
            if (stat == null) return;

            var plugin = Plugin.Instance;
            var before = stat.Value;
            var delta = before * (plugin.MonsterHealthMultiplier.Value - 1f);
            if (delta <= 0f) return;

            stat.AddValue(delta);
            Deltas[key] = delta;
            RunTally.MonstersScaled++;

            if (plugin.Verbose.Value || RunTally.First(ref RunTally.LoggedMonster))
                plugin.Log.LogInfo($"monster '{entity.name}' tier {tier}: max health {before:0} -> {stat.Value:0}");
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't scale monster health: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(ARPGEntity_Module_DifficultyScaling), nameof(ARPGEntity_Module_DifficultyScaling.RevertEntry))]
internal static class ScalingRevertPatch
{
    [HarmonyPrefix]
    private static void Prefix(ARPGEntity_Module_DifficultyScaling __instance)
    {
        try
        {
            Revert(__instance, __instance.Pointer);
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't revert monster health: {e.Message}");
        }
    }

    internal static void Revert(ARPGEntity_Module_DifficultyScaling module, IntPtr key)
    {
        if (!ScalingApplyPatch.Deltas.Remove(key, out var delta)) return;
        ImpossibleState.MaxHealthStat(module.ARPGEntity)?.SubstractValue(delta);
    }
}

[HarmonyPatch(typeof(RoomEliteSpawnScheduler), nameof(RoomEliteSpawnScheduler.Plan))]
internal static class EliteRatePatch
{
    [HarmonyPrefix]
    private static void Prefix(ref float eliteMultiplier, ref float globalElitesPerSecond)
    {
        try
        {
            if (!ImpossibleState.IsActive) return;
            var multiplier = Plugin.Instance.EliteRateMultiplier.Value;
            if (RunTally.First(ref RunTally.LoggedEliteRate))
                Plugin.Instance.Log.LogInfo(
                    $"elite rate: multiplier {eliteMultiplier:0.##} -> {eliteMultiplier * multiplier:0.##}, " +
                    $"global {globalElitesPerSecond:0.###}/s -> {globalElitesPerSecond * multiplier:0.###}/s");
            eliteMultiplier *= multiplier;
            globalElitesPerSecond *= multiplier;
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't scale the elite rate: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(ARPGEntity_Module_Health), nameof(ARPGEntity_Module_Health.TakeDamage))]
internal static class PlayerDamagePatch
{
    [HarmonyPrefix]
    private static void Prefix(ARPGEntity_Module_Health __instance, ARPGEntity from, DamagesInfo damageInfos, out float __state)
    {
        __state = -1f;
        try
        {
            if (damageInfos == null || !ImpossibleState.IsActive) return;
            var target = __instance.ARPGEntity;
            if (target == null || target.Faction != SS_FactionsDATA.PLAYER) return;
            if (from != null && from.Faction == SS_FactionsDATA.PLAYER) return;

            var plugin = Plugin.Instance;
            var multiplier = plugin.MonsterDamageMultiplier.Value;
            var red = from != null && RedElites.IsRed(from);
            if (red) multiplier *= plugin.RedEliteDamageMultiplier.Value;

            var original = damageInfos._totalDamage;
            var scaled = original * multiplier;
            __state = original;
            damageInfos._totalDamage = scaled;

            RunTally.PlayerHits++;
            RunTally.DamageOriginal += original;
            RunTally.DamageScaled += scaled;

            if (plugin.Verbose.Value || RunTally.First(ref RunTally.LoggedHit))
                plugin.Log.LogInfo($"player hit by '{(from != null ? from.name : "nothing")}'{(red ? " (red elite)" : "")}: {original:0.#} -> {scaled:0.#}");
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't scale player damage: {e.Message}");
        }
    }

    [HarmonyPostfix]
    private static void Postfix(DamagesInfo damageInfos, float __state)
    {
        try
        {
            if (__state >= 0f && damageInfos != null) damageInfos._totalDamage = __state;
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't restore player damage: {e.Message}");
        }
    }
}

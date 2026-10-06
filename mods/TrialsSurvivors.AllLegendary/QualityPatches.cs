using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace TrialsSurvivors.AllLegendary;

internal static class QualityForcer
{
    private static bool _loggedQualities;
    private static RunTally _run;

    public static SO_QualityIdentifier Force(SO_QualityIdentifier rolled, SO_CardCore card)
    {
        var plugin = Plugin.Instance;
        LogQualitiesOnce();

        var available = card.QualityIdentifiers;
        SO_QualityIdentifier? target = null;
        SO_QualityIdentifier? best = null;
        var bestRank = int.MinValue;

        foreach (var quality in available)
        {
            if (quality == null) continue;
            if (Matches(quality, plugin.Quality.Value)) target = quality;
            var rank = Rank(quality);
            if (rank > bestRank)
            {
                bestRank = rank;
                best = quality;
            }
        }

        var chosen = target ?? (plugin.FallbackToBest.Value ? best : null) ?? rolled;
        _run.Add(rolled, chosen, target != null);

        if (plugin.Verbose.Value)
            plugin.Log.LogInfo($"{Describe(card)}: rolled {Describe(rolled)} -> {Describe(chosen)}{(target == null ? " (no target tier)" : "")}");

        return chosen;
    }

    public static void EndRun()
    {
        if (_run.Cards == 0) return;
        Plugin.Instance.Log.LogInfo(
            $"run cards: {_run.Cards} drawn, {_run.Upgraded} upgraded, {_run.AlreadyTarget} rolled the target anyway, {_run.WithoutTarget} had no target tier");
        _run = default;
    }

    private static bool Matches(SO_QualityIdentifier quality, string wanted) =>
        !string.IsNullOrWhiteSpace(wanted) &&
        (quality.name.Contains(wanted, StringComparison.OrdinalIgnoreCase) ||
         (quality.NameTranslationKey ?? "").Contains(wanted, StringComparison.OrdinalIgnoreCase));

    private static int Rank(SO_QualityIdentifier quality)
    {
        var config = SO_QualityMaskConfig.Instance;
        return config != null ? config.GetBitIndex(quality) : quality.ID;
    }

    private static string Describe(SO_QualityIdentifier quality) => quality == null ? "none" : quality.name;

    private static string Describe(SO_CardCore card) => card == null ? "none" : card.name;

    private static void LogQualitiesOnce()
    {
        if (_loggedQualities) return;
        _loggedQualities = true;
        try
        {
            var config = SO_QualityMaskConfig.Instance;
            if (config == null)
            {
                Plugin.Instance.Log.LogWarning("no quality config found, ranking qualities by id");
                return;
            }

            var matched = false;
            foreach (var quality in config.Qualities)
            {
                if (quality == null) continue;
                var isTarget = Matches(quality, Plugin.Instance.Quality.Value);
                matched |= isTarget;
                Plugin.Instance.Log.LogInfo(
                    $"quality #{config.GetBitIndex(quality)} {quality.name} (id {quality.ID}, key {quality.NameTranslationKey}){(isTarget ? " <- target" : "")}");
            }

            if (!matched)
                Plugin.Instance.Log.LogWarning($"no quality matches '{Plugin.Instance.Quality.Value}', set General.Quality to one of the names above");
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't list qualities: {e.Message}");
        }
    }

    private struct RunTally
    {
        public int Cards, Upgraded, AlreadyTarget, WithoutTarget;

        public void Add(SO_QualityIdentifier rolled, SO_QualityIdentifier chosen, bool hadTarget)
        {
            Cards++;
            if (!hadTarget) WithoutTarget++;
            if (Same(rolled, chosen))
            {
                if (hadTarget) AlreadyTarget++;
            }
            else Upgraded++;
        }

        private static bool Same(SO_QualityIdentifier rolled, SO_QualityIdentifier chosen) =>
            rolled == null ? chosen == null : chosen != null && rolled.Pointer == chosen.Pointer;
    }
}

[HarmonyPatch]
internal static class GenerateCardValueFromPoolPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var type in new[] { typeof(SO_CardSkill), typeof(SO_CardStats) })
        {
            var method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Single(m => m.Name == nameof(SO_CardCore.GenerateCardValueFromPool) && !m.IsGenericMethodDefinition);
            Plugin.Instance.Log.LogInfo($"patching {type.Name}.{method.Name}");
            yield return method;
        }
    }

    [HarmonyPrefix]
    private static void Prefix(SO_CardCore __instance, ref SO_QualityIdentifier quality)
    {
        if (!Plugin.Instance.Enabled.Value || __instance == null) return;
        try
        {
            quality = QualityForcer.Force(quality, __instance);
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't force card quality: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(RunStatsTracker), nameof(RunStatsTracker.Cleanup))]
internal static class CleanupPatch
{
    [HarmonyPrefix]
    private static void Prefix() => QualityForcer.EndRun();
}

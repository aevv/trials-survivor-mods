using HarmonyLib;

namespace TrialsSurvivors.Impossible;

internal static class RunTally
{
    public static int MonstersScaled;
    public static int Elites;
    public static int RedElites;
    public static int PlayerHits;
    public static float DamageOriginal;
    public static float DamageScaled;
    public static bool LoggedMonster;
    public static bool LoggedElite;
    public static bool LoggedRedElite;
    public static bool LoggedHit;
    public static bool LoggedEliteRate;
    public static bool LoggedPaint;

    public static void Reset()
    {
        MonstersScaled = Elites = RedElites = PlayerHits = 0;
        DamageOriginal = DamageScaled = 0f;
        LoggedMonster = LoggedElite = LoggedRedElite = LoggedHit = LoggedEliteRate = LoggedPaint = false;
    }

    public static bool First(ref bool flag)
    {
        if (flag) return false;
        flag = true;
        return true;
    }
}

[HarmonyPatch(typeof(RunStatsTracker), nameof(RunStatsTracker.BeginRun))]
internal static class BeginRunPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        RunTally.Reset();
        var plugin = Plugin.Instance;
        var active = ImpossibleState.IsActive;
        plugin.Log.LogInfo(active
            ? $"run started on impossible: hp x{plugin.MonsterHealthMultiplier.Value}, damage x{plugin.MonsterDamageMultiplier.Value}, " +
              $"elite rate x{plugin.EliteRateMultiplier.Value}, max health stat {ImpossibleState.MaxHealthStatId}"
            : $"run started, impossible not active (selected={plugin.Selected.Value})");
    }
}

[HarmonyPatch(typeof(RunStatsTracker), nameof(RunStatsTracker.Cleanup))]
internal static class CleanupPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        if (RunTally.MonstersScaled == 0 && RunTally.Elites == 0 && RunTally.PlayerHits == 0) return;
        Plugin.Instance.Log.LogInfo(
            $"impossible run: {RunTally.MonstersScaled} monsters scaled, {RunTally.Elites} elites ({RunTally.RedElites} red), " +
            $"{RunTally.PlayerHits} hits on the player ({RunTally.DamageOriginal:0} -> {RunTally.DamageScaled:0} damage)");
        RunTally.Reset();
    }
}

using System;
using HarmonyLib;

namespace TrialsSurvivors.XpRates;

internal enum XpSource
{
    None,
    Orb,
    Pickup,
}

internal static class XpScaler
{
    private static XpSource _current;
    private static RunTotals _run;
    private static bool _loggedOrb;
    private static bool _loggedPickup;

    public static void Enter(XpSource source) => _current = source;

    public static void Exit() => _current = XpSource.None;

    public static float Scale(float quantity)
    {
        var source = _current;
        if (source == XpSource.None) return quantity;

        var plugin = Plugin.Instance;
        var multiplier = !plugin.Enabled.Value ? 1f
            : source == XpSource.Orb ? plugin.OrbMultiplier.Value
            : plugin.PickupMultiplier.Value;
        var scaled = quantity * multiplier;

        _run.Add(source, quantity, scaled);

        if (plugin.Verbose.Value)
            plugin.Log.LogInfo($"{source} xp {quantity:0.##} -> {scaled:0.##} (x{multiplier})");
        else if (source == XpSource.Orb && !_loggedOrb)
        {
            _loggedOrb = true;
            plugin.Log.LogInfo($"first orb xp this run: {quantity:0.##} -> {scaled:0.##} (x{multiplier})");
        }
        else if (source == XpSource.Pickup && !_loggedPickup)
        {
            _loggedPickup = true;
            plugin.Log.LogInfo($"first pickup xp this run: {quantity:0.##} -> {scaled:0.##} (x{multiplier})");
        }

        return scaled;
    }

    public static void BeginRun()
    {
        _run = default;
        _loggedOrb = false;
        _loggedPickup = false;
    }

    public static void EndRun()
    {
        if (_run.Grants == 0) return;
        Plugin.Instance.Log.LogInfo(
            $"run xp: orbs {_run.OrbOriginal:0} -> {_run.OrbScaled:0}, pickups {_run.PickupOriginal:0} -> {_run.PickupScaled:0} ({_run.Grants} grants)");
        _run = default;
    }

    private struct RunTotals
    {
        public int Grants;
        public float OrbOriginal, OrbScaled, PickupOriginal, PickupScaled;

        public void Add(XpSource source, float original, float scaled)
        {
            Grants++;
            if (source == XpSource.Orb)
            {
                OrbOriginal += original;
                OrbScaled += scaled;
            }
            else
            {
                PickupOriginal += original;
                PickupScaled += scaled;
            }
        }
    }
}

[HarmonyPatch(typeof(Collectible_XpOrbInstance), nameof(Collectible_XpOrbInstance.EndGrab))]
internal static class OrbGrabPatch
{
    [HarmonyPrefix]
    private static void Prefix() => XpScaler.Enter(XpSource.Orb);

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception)
    {
        XpScaler.Exit();
        return __exception;
    }
}

[HarmonyPatch(typeof(CollectibleEffect_XpModifier), nameof(CollectibleEffect_XpModifier.OnCollect))]
internal static class PickupCollectPatch
{
    [HarmonyPrefix]
    private static void Prefix() => XpScaler.Enter(XpSource.Pickup);

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception)
    {
        XpScaler.Exit();
        return __exception;
    }
}

[HarmonyPatch(typeof(ARPGEntity_Module_Level), nameof(ARPGEntity_Module_Level.AddXp))]
internal static class AddXpPatch
{
    [HarmonyPrefix]
    private static void Prefix(ref float quantity)
    {
        try
        {
            quantity = XpScaler.Scale(quantity);
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't scale xp: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(RunStatsTracker), nameof(RunStatsTracker.BeginRun))]
internal static class BeginRunPatch
{
    [HarmonyPostfix]
    private static void Postfix() => XpScaler.BeginRun();
}

[HarmonyPatch(typeof(RunStatsTracker), nameof(RunStatsTracker.Cleanup))]
internal static class CleanupPatch
{
    [HarmonyPrefix]
    private static void Prefix() => XpScaler.EndRun();
}

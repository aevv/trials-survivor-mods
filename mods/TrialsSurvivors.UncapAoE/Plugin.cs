using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace TrialsSurvivors.UncapAoE;

[BepInPlugin(Guid, "Trials Survivors: Uncap AoE", MyPluginInfo.PLUGIN_VERSION)]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "net.aevv.trialssurvivors.uncapaoe";

    internal static Plugin Instance = null!;

    internal ConfigEntry<bool> Enabled = null!;
    internal ConfigEntry<LimitMode> Mode = null!;
    internal ConfigEntry<float> Multiplier = null!;
    internal ConfigEntry<int> MinimumTargets = null!;
    internal ConfigEntry<bool> LogOriginalLimits = null!;
    internal ConfigEntry<bool> KeepCapOnSpawners = null!;
    internal ConfigEntry<bool> UncapAimedProjectiles = null!;

    public override void Load()
    {
        Instance = this;

        Enabled = Config.Bind("General", "Enabled", true,
            "Master switch. Turn off to restore the game's own target caps without uninstalling.");

        Mode = Config.Bind("General", "Mode", LimitMode.NoLimit,
            "How to rewrite each effect's target cap.\n" +
            "NoLimit    - hit everything in range, via the game's own -1 'no limit' path.\n" +
            "Multiplier - multiply the authored cap, keeping relative skill balance.\n" +
            "Minimum    - raise any cap below MinimumTargets up to it, leave higher caps alone.");

        Multiplier = Config.Bind("General", "Multiplier", 4f,
            new ConfigDescription("Used when Mode is Multiplier.", new AcceptableValueRange<float>(1f, 64f)));

        MinimumTargets = Config.Bind("General", "MinimumTargets", 50,
            new ConfigDescription("Used when Mode is Minimum.", new AcceptableValueRange<int>(1, 256)));

        KeepCapOnSpawners = Config.Bind("General", "KeepCapOnSpawners", true,
            "Leave the cap alone on AoEs that launch projectiles or chains at each target they hit. " +
            "Those AoEs use the cap to pick how many things to spawn, so uncapping them spawns one per enemy in range.");

        UncapAimedProjectiles = Config.Bind("General", "UncapAimedProjectiles", false,
            "Also rewrite the cap on aimed projectile launchers. Their cap is how many targets get a projectile, " +
            "so uncapping fires one projectile per enemy in range.");

        LogOriginalLimits = Config.Bind("Diagnostics", "LogOriginalLimits", false,
            "Log each distinct authored cap the game asks for, once per effect type and value. " +
            "Useful for working out the game's real numbers before deciding on a Mode.");

        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

        Log.LogInfo($"loaded; mode={Mode.Value}");
    }
}

public enum LimitMode
{
    NoLimit,
    Multiplier,
    Minimum
}

/// <summary>
/// Rewrites the authored per-effect target cap, <c>_limitDetectionCount</c>.
///
/// <para>The developers document the field as "-2 to use formula, -1 for no limit", otherwise a
/// hard cap. It feeds both the spatial query's <c>maxResults</c> and the post-detection
/// <c>SortTargets</c> trim, so rewriting the field covers the whole chain — patching the query
/// alone would leave the sort discarding the extra targets.</para>
///
/// <para>Three unrelated types declare their own copy of this field: <c>SS_Effect_AOE</c>,
/// <c>SS_Effect_AOE_Line</c> and <c>SS_Behaviour_LaunchAimedProjectile</c>. They are siblings
/// rather than a hierarchy — AOE_Line derives straight from <c>SS_Behaviour</c>, not from
/// <c>SS_Effect_AOE</c> — so each needs its own patch. Patching only the sphere AoE silently
/// leaves every line/beam and aimed-projectile effect capped.</para>
/// </summary>
internal static class LimitRewriter
{
    private const int NoLimit = -1;

    /// <summary>
    /// SS_AOEDeferredQueries batches queries into 256-wide slabs, so a finite cap above that is
    /// never honoured. Clamp so a configured number means what it says.
    /// </summary>
    private const int DeferredSlabSize = 256;

    /// <summary>
    /// Authored cap per instance, so repeated casts rewrite from the original value rather than
    /// compounding a multiplier onto an already-raised one. Keyed on the IL2CPP object pointer.
    /// </summary>
    private static readonly Dictionary<nint, (int Authored, string? SpawnReason)> AuthoredLimits = new();

    /// <summary>Effect-type/value pairs already reported, to keep diagnostics to one line each.</summary>
    private static readonly HashSet<(string, int)> LoggedLimits = new();

    /// <summary>
    /// Returns the value to write, or null to leave the effect alone. Called from a prefix, so the
    /// write lands before detection runs — a postfix would only take effect on the next cast.
    /// </summary>
    internal static int? Rewrite(string effectType, nint instance, int current, SS_Behaviour? spawnSource = null)
    {
        var plugin = Plugin.Instance;
        if (!plugin.Enabled.Value) return null;

        if (!AuthoredLimits.TryGetValue(instance, out var entry))
        {
            entry = (current, spawnSource is null ? null : SpawnDetector.FindSpawnPerTarget(spawnSource));
            AuthoredLimits[instance] = entry;

            if (plugin.LogOriginalLimits.Value && LoggedLimits.Add((effectType, entry.Authored)))
            {
                plugin.Log.LogInfo($"authored cap: {effectType} = {Describe(entry.Authored)}");
            }

            if (plugin.LogOriginalLimits.Value && entry.SpawnReason is not null && entry.Authored >= 0)
            {
                plugin.Log.LogInfo($"keeping cap {entry.Authored} on {effectType}: launches {entry.SpawnReason} per target");
            }
        }

        var authored = entry.Authored;
        if (entry.SpawnReason is not null && plugin.KeepCapOnSpawners.Value)
        {
            return authored == current ? null : authored;
        }

        // -1 is already unlimited; -2 defers to a designer formula we have no safe way to scale,
        // so leave both as the game authored them rather than guess.
        if (authored < 0) return null;

        var rewritten = plugin.Mode.Value switch
        {
            LimitMode.NoLimit => NoLimit,
            LimitMode.Multiplier => ScaleWithinSlab(authored, plugin.Multiplier.Value),
            LimitMode.Minimum => authored < plugin.MinimumTargets.Value ? plugin.MinimumTargets.Value : authored,
            _ => authored
        };

        return rewritten == current ? null : rewritten;
    }

    private static int ScaleWithinSlab(int authored, float multiplier)
    {
        var scaled = (long)(authored * multiplier);
        return scaled >= DeferredSlabSize ? DeferredSlabSize : (int)scaled;
    }

    private static string Describe(int limit) => limit switch
    {
        -1 => "-1 (no limit)",
        -2 => "-2 (formula)",
        _ => limit.ToString()
    };
}

[HarmonyPatch(typeof(SS_Effect_AOE), nameof(SS_Effect_AOE.OnPlayBehaviour))]
internal static class SphereAoePatch
{
    [HarmonyPrefix]
    private static void Prefix(SS_Effect_AOE __instance)
    {
        if (LimitRewriter.Rewrite(nameof(SS_Effect_AOE), __instance.Pointer, __instance._limitDetectionCount, __instance) is { } limit)
        {
            __instance._limitDetectionCount = limit;
        }
    }
}

[HarmonyPatch(typeof(SS_Effect_AOE_Line), nameof(SS_Effect_AOE_Line.OnPlayBehaviour))]
internal static class LineAoePatch
{
    [HarmonyPrefix]
    private static void Prefix(SS_Effect_AOE_Line __instance)
    {
        if (LimitRewriter.Rewrite(nameof(SS_Effect_AOE_Line), __instance.Pointer, __instance._limitDetectionCount, __instance) is { } limit)
        {
            __instance._limitDetectionCount = limit;
        }
    }
}

[HarmonyPatch(typeof(SS_Behaviour_LaunchAimedProjectile), nameof(SS_Behaviour_LaunchAimedProjectile.OnPlayBehaviour))]
internal static class AimedProjectilePatch
{
    [HarmonyPrefix]
    private static void Prefix(SS_Behaviour_LaunchAimedProjectile __instance)
    {
        if (!Plugin.Instance.UncapAimedProjectiles.Value) return;

        if (LimitRewriter.Rewrite(nameof(SS_Behaviour_LaunchAimedProjectile), __instance.Pointer, __instance._limitDetectionCount) is { } limit)
        {
            __instance._limitDetectionCount = limit;
        }
    }
}
